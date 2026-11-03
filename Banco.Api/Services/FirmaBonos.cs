using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Banco.Api.Data;

namespace Banco.Api.Services;

/// Llave RSA 2048 de firma del banco: se genera al primer arranque en `keys/banco-firma.pem`.
/// Firma RSA-PSS + SHA-256 sobre el mensaje canónico `serie|denominacion|emitidoEn`.
public class FirmaBonos
{
    private readonly RSA _rsa;

    public FirmaBonos(IConfiguration config)
    {
        var ruta = Path.Combine(config["Seguridad:KeysDir"] ?? "keys", "banco-firma.pem");
        _rsa = CargarOCrear(ruta);
    }

    public static RSA CargarOCrear(string ruta)
    {
        var rsa = RSA.Create(2048);
        if (File.Exists(ruta)) rsa.ImportFromPem(File.ReadAllText(ruta));
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(ruta))!);
            File.WriteAllText(ruta, rsa.ExportPkcs8PrivateKeyPem());
        }
        return rsa;
    }

    public string LlavePublicaPem() => _rsa.ExportSubjectPublicKeyInfoPem();

    /// ISO-8601 UTC sin fracciones: `2026-11-03T12:00:00Z`. emitidoEn se trunca a segundos al emitir.
    public static string Mensaje(Guid serie, int denominacion, DateTime emitidoEn) =>
        $"{serie:D}|{denominacion}|{emitidoEn.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)}";

    public string Firmar(Bono b) => Convert.ToBase64String(
        _rsa.SignData(Encoding.UTF8.GetBytes(Mensaje(b.Serie, b.Denominacion, b.EmitidoEn)), HashAlgorithmName.SHA256, RSASignaturePadding.Pss));

    public static bool Verificar(RSA llavePublica, Guid serie, int denominacion, DateTime emitidoEn, string firmaBase64)
    {
        try
        {
            return llavePublica.VerifyData(Encoding.UTF8.GetBytes(Mensaje(serie, denominacion, emitidoEn)),
                Convert.FromBase64String(firmaBase64), HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        }
        catch (FormatException) { return false; }
    }
}
