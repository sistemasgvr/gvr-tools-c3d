namespace GvrTools.Civil3D.Export.Plotting
{
    /// <summary>Where each layout's plot configuration comes from.</summary>
    public enum PlotConfigSource
    {
        /// <summary>
        /// The options in the plot panel (filled by hand, by the GVR preset, or loaded from a named
        /// page setup) are applied to every layout.
        /// </summary>
        Manual = 0,

        /// <summary>Plot each layout exactly as its own page setup says (only the CTB can be swapped).</summary>
        LayoutOwn = 1
    }

    /// <summary>Where the paper size for each layout comes from.</summary>
    public enum PlotPaperMode
    {
        /// <summary>Keep each layout's <c>CanonicalMediaName</c>.</summary>
        UseLayout = 0,

        /// <summary>Force an ISO full-bleed size (A0–A4), picking the variant that matches the orientation.</summary>
        ForceIsoFullBleed = 1,

        /// <summary>Force one exact media name from the device's list.</summary>
        SelectFromDevice = 2
    }

    /// <summary>ISO full-bleed series sizes offered in the UI.</summary>
    public enum IsoFullBleedSize
    {
        A0 = 0,
        A1 = 1,
        A2 = 2,
        A3 = 3,
        A4 = 4
    }

    /// <summary>What to plot — mirrors the Plot dialog's "Plot area".</summary>
    public enum PlotAreaMode
    {
        Extents = 0,
        Window = 1,
        Display = 2,
        Layout = 3
    }

    /// <summary>Drawing orientation as the Plot dialog shows it (Vertical / Horizontal), plus "per layout".</summary>
    public enum PlotDrawingOrientation
    {
        Landscape = 0,
        Portrait = 1,

        /// <summary>
        /// Each layout keeps its own orientation (and upside-down) from its page setup; with a forced
        /// ISO size it also picks the matching portrait/landscape variant. The right choice for a
        /// batch that mixes portrait and landscape sheets.
        /// </summary>
        FromLayout = 2
    }

    /// <summary>Paper units of the plot scale and offsets. Values match <c>PlotPaperUnit</c>.</summary>
    public enum PlotPaperUnits
    {
        Inches = 0,
        Millimeters = 1,
        Pixels = 2
    }

    /// <summary>Shaded viewport quality. Values match <c>ShadePlotResLevel</c>.</summary>
    public enum PlotShadeQuality
    {
        Draft = 0,
        Preview = 1,
        Normal = 2,
        Presentation = 3,
        Maximum = 4,
        Custom = 5
    }
}
