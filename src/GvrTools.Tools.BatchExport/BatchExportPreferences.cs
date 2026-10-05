using GvrTools.Civil3D.Export;

namespace GvrTools.Tools.BatchExport
{
    /// <summary>
    /// What the tool remembers between AutoCAD sessions. Deliberately flat scalars only — see
    /// <c>FlatFileSettingsStore</c> for why. The plot options themselves are stored separately, as
    /// a <c>PlotExportSettings</c> under <c>"batch-export-plot"</c>.
    /// </summary>
    public sealed class BatchExportPreferences
    {
        public const string StorageKey = "batch-export";

        public string OutputFolder { get; set; } = string.Empty;

        public string NamingPattern { get; set; } = NamingTokens.DefaultPattern;

        public bool OpenFolderWhenDone { get; set; } = true;
    }
}
