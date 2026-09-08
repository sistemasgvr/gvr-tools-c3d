namespace GvrTools.Civil3D.Export.Pdf
{
    /// <summary>
    /// PDF plot options that apply to every layout of a run.
    ///
    /// Deliberately smaller than Revit's equivalent: AutoCAD plots each layout using its own saved
    /// page setup (paper size, plot style, plot area) by default, so most of what Revit's
    /// PdfExportSettings had to reconstruct by hand (paper size matching, orientation, margins) is
    /// already solved by the layout's own configuration. What is left here is what a batch run
    /// actually needs to override or confirm before writing files unattended.
    /// </summary>
    public sealed class PdfExportSettings : IExportFormatSettings
    {
        public ExportFormat Format => ExportFormat.Pdf;

        /// <summary>
        /// Use each layout's own saved page setup (paper size, plot device, plot style table) as-is.
        /// When false, every layout is plotted through <see cref="PlotDeviceName"/> instead,
        /// overriding whatever device the page setup points at.
        /// </summary>
        public bool UseLayoutPageSetup { get; set; } = true;

        /// <summary>
        /// Plot device (.pc3) used when <see cref="UseLayoutPageSetup"/> is false, or when a layout
        /// has no usable page setup at all. "DWG To PDF.pc3" ships with every AutoCAD-based product
        /// since 2007 and needs no separate installation, unlike Revit's PDF24 dependency on 2021.
        /// </summary>
        public string PlotDeviceName { get; set; } = "DWG To PDF.pc3";

        /// <summary>Scale the drawing to fill the page.</summary>
        public bool FitToPaper { get; set; } = true;

        /// <summary>
        /// When true, every selected layout is plotted as one page of a single multi-page PDF instead
        /// of one file per layout. Uses AutoCAD's multi-sheet plot pipeline (one BeginDocument, one
        /// page per layout).
        /// </summary>
        public bool CombineIntoSinglePdf { get; set; }

        /// <summary>Plots what is inside the layout's own plot area (defined on the page setup) rather than a manual window.</summary>
        public bool PlotWithLineweights { get; set; } = true;

        /// <summary>Named plot style table (.ctb/.stb) override, or empty to keep the layout's own.</summary>
        public string PlotStyleTableOverride { get; set; } = string.Empty;
    }
}
