namespace GvrTools.Civil3D.Export.Pdf
{
    /// <summary>Where the paper size for each layout comes from.</summary>
    public enum PdfPaperMode
    {
        /// <summary>Keep each layout's <c>CanonicalMediaName</c>.</summary>
        UseLayout = 0,

        /// <summary>Force an ISO full-bleed size (A0–A4) on every layout.</summary>
        ForceIsoFullBleed = 1,

        /// <summary>Force a specific media name from the selected plotter's list.</summary>
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

    /// <summary>What to plot — mirrors AutoCAD's Plot dialog "Plot area".</summary>
    public enum PdfPlotArea
    {
        Extents = 0,
        Window = 1,
        Display = 2,
        Layout = 3
    }

    /// <summary>Drawing orientation on the paper (AutoCAD Plot dialog).</summary>
    public enum PdfPlotOrientation
    {
        Landscape = 0,
        Portrait = 1
    }
}
