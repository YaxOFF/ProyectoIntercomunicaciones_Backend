using System.Security.Cryptography;

namespace Tienda.Api.Services;

/// Paquete que manda la app: llave AES-256 cifrada con RSA-OAEP-SHA256 de la tienda + datos AES-GCM.
public record PaqueteCifrado(string LlaveCifrada, string Nonce, string Datos);

/// Llave RSA 2048 de cifrado de la tienda: se genera al primer arranque en `keys/tienda-cifrado.pem`.
public class DescifradoHibrido
{
    private const int TagBytes = 16;
    private readonly RSA _rsa = RSA.Create(2048);

    public DescifradoHibrido(IConfiguration config)
    {
        var ruta = Path.Combine(config["Seguridad:KeysDir"] ?? "keys", "tienda-cifrado.pem");
        if (File.Exists(ruta)) _rsa.ImportFromPem(File.ReadAllText(ruta));
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(ruta))!);
            File.WriteAllText(ruta, _rsa.ExportPkcs8PrivateKeyPem());
        }
    }

    public string LlavePublicaPem() => _rsa.ExportSubjectPublicKeyInfoPem();

    /// null si el paquete está mal formado, fue alterado (tag GCM) o no se cifró para esta llave.
    public byte[]? Descifrar(PaqueteCifrado p)
    {
        try
        {
            var llave = _rsa.Decrypt(Convert.FromBase64String(p.LlaveCifrada), RSAEncryptionPadding.OaepSHA256);
            var nonce = Convert.FromBase64String(p.Nonce);
            var datos = Convert.FromBase64String(p.Datos); // cifrado | tag16
            if (llave.Length != 32 || nonce.Length != 12 || datos.Length < TagBytes) return null;
            var plano = new byte[datos.Length - TagBytes];
            using var aes = new AesGcm(llave, TagBytes);
            aes.Decrypt(nonce, datos.AsSpan(0, plano.Length), datos.AsSpan(plano.Length), plano);
            return plano;
        }
        catch (Exception e) when (e is FormatException or CryptographicException) { return null; }
    }
}
