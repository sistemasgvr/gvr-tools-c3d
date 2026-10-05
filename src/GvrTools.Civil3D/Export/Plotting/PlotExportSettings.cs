namespace GvrTools.Civil3D.Export.Plotting
{
    /// <summary>
    /// Plot options that apply to every layout of a run — the batch equivalent of everything the
    /// Civil 3D Plot dialog lets you set, plus the GVR extras (ISO full bleed by orientation, CTB
    /// replacement, common plot window, one combined file).
    ///
    /// Defaults match the workflow agreed for GVR Tools: DWG To PDF.pc3, ISO full bleed A4,
    /// Extents, 1:1, centered, plot styles / lineweights / transparency / paperspace last.
    /// See <c>docs/PLOT_API_NOTES.md</c>.
    /// </summary>
    public sealed class PlotExportSettings : IExportFormatSettings
    {
        public ExportFormat Format => ExportFormat.Plot;

        public PlotConfigSource Source { get; set; } = PlotConfigSource.Manual;

        // ---------------------------------------------------------------- printer / plotter

        /// <summary>Device as AutoCAD lists it: a Windows printer name or a ".pc3" file name.</summary>
        public string PlotDeviceName { get; set; } = PlotDeviceRepository.DefaultPdfDeviceName;

        /// <summary>
        /// "Plot to file" for devices that allow both (Windows printers). Devices that must plot to
        /// a file (PDF/DWF/PNG .pc3) always do; devices that cannot never do.
        /// </summary>
        public bool PlotToFile { get; set; }

        /// <summary>Copies sent to a printer. Ignored when plotting to a file.</summary>
        public int Copies { get; set; } = 1;

        // ---------------------------------------------------------------- paper

        public PlotPaperMode PaperMode { get; set; } = PlotPaperMode.ForceIsoFullBleed;

        /// <summary>Used when <see cref="PaperMode"/> is <see cref="PlotPaperMode.ForceIsoFullBleed"/>.</summary>
        public IsoFullBleedSize IsoFullBleedSize { get; set; } = IsoFullBleedSize.A4;

        /// <summary>Used when <see cref="PaperMode"/> is <see cref="PlotPaperMode.SelectFromDevice"/>.</summary>
        public string SelectedCanonicalMediaName { get; set; } = string.Empty;

        // ---------------------------------------------------------------- plot area

        public PlotAreaMode PlotArea { get; set; } = PlotAreaMode.Extents;

        /// <summary>
        /// With <see cref="PlotAreaMode.Window"/>: true plots the same paper-space window on every
        /// layout (picked once or loaded from a page setup); false uses each layout's own window.
        /// </summary>
        public bool UseCommonWindow { get; set; }

        public double WindowMinX { get; set; }

        public double WindowMinY { get; set; }

        public double WindowMaxX { get; set; }

        public double WindowMaxY { get; set; }

        // ---------------------------------------------------------------- scale

        /// <summary>"Fit to paper" (ScaleToFit). When true the scale below is ignored.</summary>
        public bool FitToPaper { get; set; }

        /// <summary>Paper side of the scale ("1" in 1:100).</summary>
        public double ScalePaperUnits { get; set; } = 1.0;

        /// <summary>Drawing side of the scale ("100" in 1:100).</summary>
        public double ScaleDrawingUnits { get; set; } = 1.0;

        /// <summary>Units of the paper side of the scale and of the plot offsets.</summary>
        public PlotPaperUnits PaperUnits { get; set; } = PlotPaperUnits.Millimeters;

        /// <summary>Maps to <c>PlotSettings.ScaleLineweights</c>.</summary>
        public bool ScaleLineweights { get; set; }

        // ---------------------------------------------------------------- offset

        public bool CenterPlot { get; set; } = true;

        /// <summary>Plot offset in <see cref="PaperUnits"/>. Ignored when <see cref="CenterPlot"/>.</summary>
        public double OffsetX { get; set; }

        public double OffsetY { get; set; }

        // ---------------------------------------------------------------- orientation

        public PlotDrawingOrientation Orientation { get; set; } = PlotDrawingOrientation.FromLayout;

        /// <summary>"Plot upside-down" (adds 180°). Ignored with <see cref="PlotDrawingOrientation.FromLayout"/>.</summary>
        public bool PlotUpsideDown { get; set; }

        // ---------------------------------------------------------------- plot style table + options

        /// <summary>Named plot style table (.ctb/.stb) override, or empty to keep the layout's own.</summary>
        public string PlotStyleTableOverride { get; set; } = string.Empty;

        /// <summary>Maps to <c>PlotSettings.PrintLineweights</c>.</summary>
        public bool PlotObjectLineweights { get; set; } = true;

        public bool PlotTransparency { get; set; } = true;

        /// <summary>Maps to <c>PlotSettings.PlotPlotStyles</c>.</summary>
        public bool PlotWithPlotStyles { get; set; } = true;

        /// <summary>
        /// "Plot paperspace last". Maps to <c>PlotSettings.DrawViewportsFirst</c> (viewports first,
        /// then paper space) per Autodesk DevGuide samples.
        /// </summary>
        public bool PlotPaperspaceLast { get; set; } = true;

        /// <summary>"Hide paperspace objects". Maps to <c>PlotSettings.PlotHidden</c>.</summary>
        public bool HidePaperspaceObjects { get; set; }

        // ---------------------------------------------------------------- shaded viewports

        public PlotShadeQuality ShadeQuality { get; set; } = PlotShadeQuality.Normal;

        /// <summary>DPI when <see cref="ShadeQuality"/> is <see cref="PlotShadeQuality.Custom"/>.</summary>
        public int ShadeCustomDpi { get; set; } = 300;

        // ---------------------------------------------------------------- output

        /// <summary>One multi-sheet file for the whole run (PDF / DWF devices only).</summary>
        public bool CombineIntoSingleFile { get; set; }
    }
}
