namespace GvrTools.Licensing.Entitlements
{
    /// <summary>
    /// Puerta de entrada que usan el ribbon y las tools para preguntar por un feature antes de
    /// mostrarse o de correr (docs/LICENSING_PLAN.md, "Catálogo de features"). Nunca lanza: si no
    /// hay licencia válida cacheada, <see cref="CanUse"/> simplemente devuelve false.
    /// </summary>
    public interface IEntitlementService
    {
        /// <summary>true si el plan activo incluye el feature (ej. "tool.batch_export", "format.dwg").</summary>
        bool CanUse(string featureCode);

        /// <summary>Cuota restante para un feature numérico (ej. "quota.sheets_per_month"), o -1 si es ilimitado.</summary>
        int Remaining(string featureCode);

        /// <summary>
        /// Tope del periodo para un feature de cuota (ej. "quota.sheets_per_month"), o -1 si es
        /// ilimitado. Viene del companion `{code}.limit` en el blob; 0 si no hay licencia/dato.
        /// </summary>
        int QuotaLimit(string featureCode);

        /// <summary>
        /// Reserva/consume cuota antes de iniciar un lote. Devuelve false si no alcanza -- el
        /// llamador debe bloquear la operación completa, nunca empezarla a medias (regla 3 de
        /// "Reglas de consumo" en el plan).
        /// </summary>
        bool TryConsume(string featureCode, int quantity);

        /// <summary>Valor crudo (no numérico) de un feature del blob, ej. FeatureCodes.SupportEmail. Null si no hay licencia o no existe.</summary>
        string GetString(string featureCode);
    }
}
