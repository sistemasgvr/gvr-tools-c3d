namespace GvrTools.Civil3D.Export.Pdf
{
    /// <summary>
    /// PDF plot options that apply to every layout of a run.
    ///
    /// Defaults match the Civil 3D workflow agreed for GVR Tools (Extents, 1:1, centered, forced
    /// PC3, plot styles / lineweights / transparency / paperspace-last, ISO full bleed A4).
    /// Every option is editable from the UI. See <c>docs/PLOT_API_NOTES.md</c>.
    /// </summary>
    public sealed class PdfExportSettings : IExportFormatSettings
    {
        public ExportFormat Format => ExportFormat.Pdf;

        /// <summary>
        /// When true (default), every layout is plotted through <see cref="PlotDeviceName"/>.
        /// </summary>
        public bool ForcePlotDevice { get; set; } = true;

        /// <summary>Plot device (.pc3). Default ships with every AutoCAD-based product.</summary>
        public string PlotDeviceName { get; set; } = "DWG To PDF.pc3";

        public PdfPaperMode PaperMode { get; set; } = PdfPaperMode.ForceIsoFullBleed;

        /// <summary>Used when <see cref="PaperMode"/> is <see cref="PdfPaperMode.ForceIsoFullBleed"/>.</summary>
        public IsoFullBleedSize IsoFullBleedSize { get; set; } = IsoFullBleedSize.A4;

        /// <summary>
        /// Canonical media name when <see cref="PaperMode"/> is <see cref="PdfPaperMode.SelectFromDevice"/>.
        /// </summary>
        public string SelectedCanonicalMediaName { get; set; } = string.Empty;

        /// <summary>Plot area. Default Extents (meeting workflow).</summary>
        public PdfPlotArea PlotArea { get; set; } = PdfPlotArea.Extents;

        /// <summary>
        /// When true (default), force standard scale 1:1 + centered + not Fit-to-paper.
        /// When false, scale/center follow Fit / custom / layout copy.
        /// </summary>
        public bool ForcePlotPreset { get; set; } = true;

        /// <summary>Scale drawing to fill the page (ScaleToFit). Ignored when <see cref="ForcePlotPreset"/>.</summary>
        public bool FitToPaper { get; set; }

        /// <summary>Center the plot. Applied with the preset or when preset is off.</summary>
        public bool CenterPlot { get; set; } = true;

        /// <summary>
        /// When true (and preset/Fit off), use <see cref="CustomScaleNumerator"/> /
        /// <see cref="CustomScaleDenominator"/> via <c>SetCustomPrintScale</c>.
        /// </summary>
        public bool UseCustomScale { get; set; }

        /// <summary>Paper units side of custom scale (e.g. 1 mm).</summary>
        public double CustomScaleNumerator { get; set; } = 1.0;

        /// <summary>Drawing units side of custom scale (e.g. 1 unit).</summary>
        public double CustomScaleDenominator { get; set; } = 1.0;

        /// <summary>Maps to <c>PlotSettings.ScaleLineweights</c>.</summary>
        public bool ScaleLineweights { get; set; }

        /// <summary>Maps to <c>SetPlotRotation</c> (Landscape = Degrees000, Portrait = Degrees090).</summary>
        public PdfPlotOrientation PlotOrientation { get; set; } = PdfPlotOrientation.Landscape;

        public bool PlotTransparency { get; set; } = true;

        /// <summary>Maps to <c>PlotSettings.PrintLineweights</c>.</summary>
        public bool PlotObjectLineweights { get; set; } = true;

        /// <summary>Maps to <c>PlotSettings.PlotPlotStyles</c>.</summary>
        public bool PlotWithPlotStyles { get; set; } = true;

        /// <summary>
        /// UI “Plot paperspace last”. Maps to <c>PlotSettings.DrawViewportsFirst</c> (viewports
        /// first, then paper space) per Autodesk DevGuide samples.
        /// </summary>
        public bool PlotPaperspaceLast { get; set; } = true;

        public bool CombineIntoSinglePdf { get; set; }

        /// <summary>Named plot style table (.ctb/.stb) override, or empty to keep the layout's own.</summary>
        public string PlotStyleTableOverride { get; set; } = string.Empty;
    }
}
