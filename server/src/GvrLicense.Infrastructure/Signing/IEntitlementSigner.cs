namespace GvrLicense.Infrastructure.Signing;

/// <summary>
/// Firma ECDsa P-256 (docs/LICENSING_PLAN.md, "Tokens y gracia offline" -- se eligió sobre Ed25519
/// porque el cliente net48 lo verifica con System.Security.Cryptography nativo, sin NuGet).
/// </summary>
public interface IEntitlementSigner
{
    byte[] Sign(byte[] data);

    /// <summary>SubjectPublicKeyInfo en base64 -- esto es lo que se embebe en GvrTools.Licensing/Crypto.</summary>
    string GetPublicKeyBase64();
}
