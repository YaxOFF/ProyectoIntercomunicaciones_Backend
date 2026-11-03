using System.Security.Cryptography;
using System.Text;

namespace Banco.Api.Services;

/// AES-256-GCM para datos sensibles en BD. Formato base64(nonce[12] | cifrado | tag[16]).
public class CifradoAes
{
    private readonly byte[] _llave;

    public CifradoAes(IConfiguration config)
    {
        _llave = Convert.FromBase64String(config["Seguridad:AesKey"]
            ?? throw new InvalidOperationException("Falta Seguridad:AesKey"));
        if (_llave.Length != 32) throw new InvalidOperationException("Seguridad:AesKey debe ser de 32 bytes (base64)");
    }

    public string Cifrar(string texto)
    {
        var plano = Encoding.UTF8.GetBytes(texto);
        var salida = new byte[12 + plano.Length + 16];
        var nonce = salida.AsSpan(0, 12);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_llave, 16);
        aes.Encrypt(nonce, plano, salida.AsSpan(12, plano.Length), salida.AsSpan(12 + plano.Length));
        return Convert.ToBase64String(salida);
    }

    public string Descifrar(string base64)
    {
        var datos = Convert.FromBase64String(base64);
        var largo = datos.Length - 28;
        var plano = new byte[largo];
        using var aes = new AesGcm(_llave, 16);
        aes.Decrypt(datos.AsSpan(0, 12), datos.AsSpan(12, largo), datos.AsSpan(12 + largo), plano);
        return Encoding.UTF8.GetString(plano);
    }

    public static string NuevaClabe() => "002180" + RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6")
        + RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    public static string Enmascarar(string clabe) => new string('*', clabe.Length - 4) + clabe[^4..];
}
