namespace GvrTools.Licensing.Entitlements
{
    /// <summary>Códigos estables del catálogo (docs/LICENSING_PLAN.md).</summary>
    public static class FeatureCodes
    {
        public const string ToolBatchExport = "tool.batch_export";
        public const string FormatPdf = "format.pdf";
        public const string FormatDwg = "format.dwg";
        public const string FormatPdfDwg = "format.pdf_dwg";
        public const string QuotaSheetsPerMonth = "quota.sheets_per_month";
        public const string LimitSheetsPerBatch = "limit.sheets_per_batch";

        /// <summary>
        /// No es un feature de plan -- LicenseEngine.BuildSignedBlobAsync lo agrega igual en todo
        /// blob, tomado de Admin → Configuración (AppSettings.SupportEmail), para que el correo de
        /// soporte que ve el add-in sea el que edita el admin y no uno fijo en el código.
        /// </summary>
        public const string SupportEmail = "meta.support_email";

        /// <summary>Nombre visible del plan (ej. "Free", "Pro") -- PlanCode ("free"/"pro") sigue siendo lo que usa la lógica.</summary>
        public const string PlanDisplayName = "meta.plan_display_name";
    }
}
