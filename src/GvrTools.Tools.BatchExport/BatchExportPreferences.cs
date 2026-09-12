using GvrTools.Civil3D.Export;

namespace GvrTools.Tools.BatchExport
{
    /// <summary>
    /// What the tool remembers between AutoCAD sessions. Deliberately flat scalars only — see
    /// <c>FlatFileSettingsStore</c> for why.
    /// </summary>
    public sealed class BatchExportPreferences
    {
        public const string StorageKey = "batch-export";

        public string OutputFolder { get; set; } = string.Empty;

        public string NamingPattern { get; set; } = NamingTokens.DefaultPattern;

        public bool OpenFolderWhenDone { get; set; } = true;

        public string PdfPlotDeviceName { get; set; } = PlotDeviceRepository.DefaultPdfDeviceName;

        public bool PdfPlotTransparency { get; set; } = true;

        public bool PdfCombineIntoSinglePdf { get; set; }

        /// <summary>Plot style table (.ctb/.stb) chosen last time, or empty to keep each layout's own.</summary>
        public string PdfPlotStyleTable { get; set; } = string.Empty;
    }
}
