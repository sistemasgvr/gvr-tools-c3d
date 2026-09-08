using GvrTools.Licensing.Entitlements;

namespace GvrTools.Licensing.Crypto
{
    /// <summary>
    /// Verifica la firma ECDsa P-256 del blob de entitlements contra la clave pública embebida en
    /// el add-in (ver docs/LICENSING_PLAN.md, "Tokens y gracia offline" -- ECDsa P-256 se eligió
    /// sobre Ed25519 porque es nativo en System.Security.Cryptography tanto en net48 como en
    /// net8.0-windows; ninguna versión de Revit soportada obliga a meter una librería de terceros).
    /// </summary>
    public interface IEntitlementSignatureVerifier
    {
        /// <summary>
        /// Verifica la firma sobre <paramref name="rawJson"/> y, si es válida, parsea el blob.
        /// Devuelve false (sin lanzar) ante cualquier fallo de firma o de formato: un blob no
        /// verificado nunca debe tratarse como entitlements válidos.
        /// </summary>
        bool TryVerify(string rawJson, byte[] signature, out EntitlementBlob blob);
    }
}
