namespace GvrTools.Licensing.Storage
{
    /// <summary>
    /// Persiste el blob firmado + device id en <c>%APPDATA%\GVR\GvrTools\license.dat</c>
    /// (docs/LICENSING_PLAN.md, Pieza 2). Guarda el JSON firmado tal cual llegó del servidor, no un
    /// objeto ya parseado, para que la verificación de firma siempre corra sobre los mismos bytes
    /// que el servidor firmó.
    /// </summary>
    public interface ILicenseCacheStore
    {
        bool TryLoad(out string rawJson, out byte[] signature);

        void Save(string rawJson, byte[] signature);

        /// <summary>Usado por "Desactivar este PC": borra el cache local tras liberar el seat en el servidor.</summary>
        void Clear();
    }
}
