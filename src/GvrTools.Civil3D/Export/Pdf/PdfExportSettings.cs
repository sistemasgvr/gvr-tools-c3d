namespace GvrTools.Civil3D.Export.Pdf
{
    /// <summary>
    /// PDF plot options that apply to every layout of a run.
    ///
    /// Paper size (<c>CanonicalMediaName</c>) always comes from each layout. What this class
    /// overrides — when the corresponding flags are on — is the plot device (.pc3), the plot style
    /// table (.ctb/.stb), the Extents / 1:1 / centered preset, and plot transparency. See
    /// <c>docs/PLOT_API_NOTES.md</c>.
    /// </summary>
    public sealed class PdfExportSettings : IExportFormatSettings
    {
        public ExportFormat Format => ExportFormat.Pdf;

        /// <summary>
        /// When true (default), every layout is plotted through <see cref="PlotDeviceName"/>,
        /// overriding whatever device the layout's page setup points at. This is what lets a custom
        /// high-DPI "DWG To PDF HQ.pc3" reach every sheet — AutoCAD's built-in Batch Plot often
        /// fails to honour a modified PC3 across a sheet set.
        /// </summary>
        public bool ForcePlotDevice { get; set; } = true;

        /// <summary>
        /// Plot device (.pc3) used when <see cref="ForcePlotDevice"/> is true, or when a layout has
        /// no usable PDF device. "DWG To PDF.pc3" ships with every AutoCAD-based product.
        /// </summary>
        public string PlotDeviceName { get; set; } = "DWG To PDF.pc3";

        /// <summary>
        /// When true (default), force Extents + standard scale 1:1 + centered plot (Fit to paper
        /// off). Matches the Civil 3D workflow agreed for GVR Tools v1.
        /// </summary>
        public bool ForcePlotPreset { get; set; } = true;

        /// <summary>
        /// When true (default), set <c>PlotSettings.PlotTransparency</c> so transparent objects
        /// reach the PDF the same way the Plot dialog checkbox does.
        /// </summary>
        public bool PlotTransparency { get; set; } = true;

        /// <summary>
        /// When true, every selected layout is plotted as one page of a single multi-page PDF instead
        /// of one file per layout. Uses AutoCAD's multi-sheet plot pipeline (one BeginDocument, one
        /// page per layout). Only used by the open-drawing batch tool.
        /// </summary>
        public bool CombineIntoSinglePdf { get; set; }

        /// <summary>Named plot style table (.ctb/.stb) override, or empty to keep the layout's own.</summary>
        public string PlotStyleTableOverride { get; set; } = string.Empty;
    }
}
