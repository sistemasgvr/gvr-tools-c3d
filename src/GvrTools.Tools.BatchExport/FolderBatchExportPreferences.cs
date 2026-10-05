namespace GvrTools.Tools.BatchExport
{
    /// <summary>
    /// What the multi-drawing exporter remembers between AutoCAD sessions. The plot options are
    /// stored separately, as a <c>PlotExportSettings</c> under <c>"folder-batch-export-plot"</c>.
    /// </summary>
    public sealed class FolderBatchExportPreferences
    {
        public const string StorageKey = "folder-batch-export";

        public string SourceFolder { get; set; } = string.Empty;

        public bool IncludeSubfolders { get; set; }

        public string OutputFolder { get; set; } = string.Empty;

        public string NamingPattern { get; set; } = "{DrawingTitle}-{LayoutName}";

        public bool OpenFolderWhenDone { get; set; } = true;
    }
}
