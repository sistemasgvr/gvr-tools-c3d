using GvrTools.Civil3D.Export;

namespace GvrTools.Tools.BatchExport
{
    /// <summary>What the multi-drawing exporter remembers between AutoCAD sessions.</summary>
    public sealed class FolderBatchExportPreferences
    {
        public const string StorageKey = "folder-batch-export";

        public string SourceFolder { get; set; } = string.Empty;

        public bool IncludeSubfolders { get; set; }

        public string OutputFolder { get; set; } = string.Empty;

        public string NamingPattern { get; set; } = "{DrawingTitle}-{LayoutName}";

        public bool OpenFolderWhenDone { get; set; } = true;

        public string PdfPlotDeviceName { get; set; } = PlotDeviceRepository.DefaultPdfDeviceName;

        public bool PdfPlotTransparency { get; set; } = true;

        /// <summary>Plot style table (.ctb/.stb) chosen last time, or empty to keep each layout's own.</summary>
        public string PdfPlotStyleTable { get; set; } = string.Empty;
    }
}
