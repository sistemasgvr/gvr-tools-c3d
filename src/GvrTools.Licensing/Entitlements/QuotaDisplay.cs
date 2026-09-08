using System;
using System.Globalization;

namespace GvrTools.Licensing.Entitlements
{
    /// <summary>
    /// Texto de cuota compartido por footer de tools y ventana Cuenta / Cambiar plan
    /// (UI_FREEMIUM_PLAN.md §3.3: "Usadas X de Y" / "ilimitado").
    /// </summary>
    public static class QuotaDisplay
    {
        /// <summary>
        /// "Usadas X de Y este mes", "ilimitado", o vacío si no hay licencia con dato útil.
        /// Si el blob es antiguo (sin companion `.limit`), cae a "N lámina(s) restante(s)".
        /// </summary>
        public static string FormatSheetsUsage(IEntitlementService entitlements)
        {
            if (entitlements == null) return string.Empty;

            int remaining = entitlements.Remaining(FeatureCodes.QuotaSheetsPerMonth);
            int limit = entitlements.QuotaLimit(FeatureCodes.QuotaSheetsPerMonth);

            if (remaining < 0 || limit < 0)
                return "ilimitado";

            if (limit > 0)
            {
                int used = Math.Max(0, limit - remaining);
                return string.Format(
                    CultureInfo.CurrentCulture,
                    "Usadas {0} de {1} este mes",
                    used,
                    limit);
            }

            // Blob legado sin `{code}.limit`.
            return remaining.ToString(CultureInfo.CurrentCulture) + " lámina(s) restante(s) este mes";
        }

        /// <summary>true si el plan es free y se usó ≥80% de la cuota (aviso suave + CTA).</summary>
        public static bool IsNearLimit(IEntitlementService entitlements, string planCode)
        {
            if (entitlements == null) return false;
            if (!string.Equals(planCode, "free", StringComparison.OrdinalIgnoreCase)) return false;

            int remaining = entitlements.Remaining(FeatureCodes.QuotaSheetsPerMonth);
            int limit = entitlements.QuotaLimit(FeatureCodes.QuotaSheetsPerMonth);
            if (limit <= 0 || remaining < 0) return false;

            int used = Math.Max(0, limit - remaining);
            return used >= (int)Math.Ceiling(limit * 0.8);
        }
    }
}
