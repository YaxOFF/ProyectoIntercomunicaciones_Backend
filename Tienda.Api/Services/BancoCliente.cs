using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Tienda.Api.Services;

public record BonoFirmado(Guid Serie, int Denominacion, DateTime EmitidoEn, string Firma);
public record RespuestaCanje(List<BonoFirmado>? Canjeados, int Total, List<BonoFirmado>? Cambio, int CambioEnCuenta);

/// Cliente HTTP tienda → banco: llave pública de firma (cacheada) y canje con `X-Api-Key`.
public class BancoCliente(HttpClient http, IConfiguration config)
{
    // ponytail: cache estática para todo el proceso; si el banco rota su llave hay que reiniciar la tienda.
    private static RSA? _llaveBanco;

    public async Task<RSA> LlavePublica()
    {
        if (_llaveBanco is not null) return _llaveBanco;
        var rsa = RSA.Create();
        rsa.ImportFromPem(await http.GetStringAsync("llave-publica"));
        return _llaveBanco = rsa;
    }

    /// Mismo mensaje canónico que firma el banco: `serie|denominacion|yyyy-MM-ddTHH:mm:ssZ`, RSA-PSS SHA-256.
    public static bool Verificar(RSA llave, BonoFirmado b)
    {
        var mensaje = $"{b.Serie:D}|{b.Denominacion}|{b.EmitidoEn.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)}";
        try
        {
            return llave.VerifyData(Encoding.UTF8.GetBytes(mensaje), Convert.FromBase64String(b.Firma),
                HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        }
        catch (FormatException) { return false; }
    }

    public async Task<(HttpStatusCode status, RespuestaCanje? respuesta)> Canjear(IEnumerable<Guid> series, int cobro)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "bonos/canjear") { Content = JsonContent.Create(new { series, cobro }) };
        req.Headers.Add("X-Api-Key", config["Seguridad:ApiKeyTienda"] ?? throw new InvalidOperationException("Falta Seguridad:ApiKeyTienda"));
        var r = await http.SendAsync(req);
        return (r.StatusCode, r.IsSuccessStatusCode ? await r.Content.ReadFromJsonAsync<RespuestaCanje>() : null);
    }
}
