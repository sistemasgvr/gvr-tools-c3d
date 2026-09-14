using GvrTools.Civil3D.Export;
using GvrTools.Civil3D.Export.Pdf;

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

        public int PdfPaperMode { get; set; } = (int)GvrTools.Civil3D.Export.Pdf.PdfPaperMode.ForceIsoFullBleed;

        public int PdfIsoFullBleedSize { get; set; } = (int)GvrTools.Civil3D.Export.Pdf.IsoFullBleedSize.A4;

        public string PdfSelectedMediaName { get; set; } = string.Empty;

        public int PdfPlotArea { get; set; } = (int)GvrTools.Civil3D.Export.Pdf.PdfPlotArea.Extents;

        public bool PdfForcePlotPreset { get; set; } = true;

        public bool PdfFitToPaper { get; set; }

        public bool PdfCenterPlot { get; set; } = true;

        public bool PdfUseCustomScale { get; set; }

        public double PdfCustomScaleNumerator { get; set; } = 1.0;

        public double PdfCustomScaleDenominator { get; set; } = 1.0;

        public bool PdfScaleLineweights { get; set; }

        public int PdfPlotOrientation { get; set; } = (int)GvrTools.Civil3D.Export.Pdf.PdfPlotOrientation.Landscape;

        public bool PdfPlotTransparency { get; set; } = true;

        public bool PdfPlotObjectLineweights { get; set; } = true;

        public bool PdfPlotWithPlotStyles { get; set; } = true;

        public bool PdfPlotPaperspaceLast { get; set; } = true;

        public bool PdfCombineIntoSinglePdf { get; set; }

        /// <summary>Plot style table (.ctb/.stb) chosen last time, or empty to keep each layout's own.</summary>
        public string PdfPlotStyleTable { get; set; } = string.Empty;
    }
}
