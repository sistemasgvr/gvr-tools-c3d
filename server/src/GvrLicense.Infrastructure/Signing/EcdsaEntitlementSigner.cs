using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;

namespace GvrLicense.Infrastructure.Signing;

/// <summary>
/// Lee la clave privada de <c>Signing:PrivateKeyPem</c> (variable de entorno
/// <c>Signing__PrivateKeyPem</c> en EasyPanel -- nunca en appsettings.json versionado). Falla rápido
/// al arrancar si falta: un API de licencias sin clave de firma no debería ni levantar, porque
/// ningún cliente podría activar ni renovar.
/// </summary>
public sealed class EcdsaEntitlementSigner : IEntitlementSigner, IDisposable
{
    private readonly ECDsa _key;

    public EcdsaEntitlementSigner(IConfiguration configuration)
    {
        var pem = PemNormalizer.Normalize(configuration["Signing:PrivateKeyPem"]);
        if (string.IsNullOrWhiteSpace(pem))
        {
            throw new InvalidOperationException(
                "Signing:PrivateKeyPem no está configurado. Generar un par de claves con " +
                "server/tools/GenerateSigningKey y cargar la privada como Signing__PrivateKeyPem " +
                "en las variables de entorno (ver server/README.md).");
        }

        _key = ECDsa.Create();
        _key.ImportFromPem(pem);
    }

    public byte[] Sign(byte[] data) => _key.SignData(data, HashAlgorithmName.SHA256);

    /// <summary>
    /// Punto (X, Y) crudo de 64 bytes en base64 -- no SubjectPublicKeyInfo. El cliente (net48) no
    /// tiene ImportSubjectPublicKeyInfo (es de .NET 5+); ECParameters con el punto crudo es el único
    /// formato que ambos frameworks importan. Debe coincidir con
    /// src/GvrTools.Licensing/Crypto/EmbeddedPublicKey.Base64.
    /// </summary>
    public string GetPublicKeyBase64()
    {
        var parameters = _key.ExportParameters(includePrivateParameters: false);
        var rawPoint = new byte[64];
        parameters.Q.X!.CopyTo(rawPoint, 0);
        parameters.Q.Y!.CopyTo(rawPoint, 32);
        return Convert.ToBase64String(rawPoint);
    }

    public void Dispose() => _key.Dispose();
}
