namespace GvrTools.Licensing.Crypto
{
    /// <summary>
    /// Clave pública ECDsa P-256 del License API (punto X||Y crudo, 64 bytes, base64).
    /// Debe coincidir con la privada en Signing:PrivateKeyPem del servidor.
    ///
    /// Par de DESARROLLO generado con server/tools/GenerateSigningKey. Antes de la primera venta
    /// genera un par de producción, pon la privada SOLO en EasyPanel (Signing__PrivateKeyPem) y
    /// reemplaza este valor (nunca subas la privada a git).
    /// </summary>
    public static class EmbeddedPublicKey
    {
        public const string Base64 =
            "5hS2t9mYzXWJG0ksI3ZxqaWxjR4G2wSnyttZMH1EzD6OTm2acjPBDDTjS50+3yAwl0bjselEoM1YlycS4qLeDw==";
    }
}
