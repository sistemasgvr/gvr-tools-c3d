using GvrTools.Civil3D.Export.Plotting;
using GvrTools.Core.Settings;

namespace GvrTools.Tools.BatchExport
{
    /// <summary>
    /// The PDF-only preferences of the first releases, stored as "Pdf*" keys next to the window
    /// preferences ("batch-export" / "folder-batch-export"). Read once so a user who had, say,
    /// "Usar tamaño del layout" keeps it instead of silently falling back to the GVR preset
    /// (ISO A4 at 1:1, which cuts any larger layout).
    ///
    /// The keys disappear the first time the new version saves (window and plot preferences are
    /// written together), so the migration runs only until the user plots once.
    /// </summary>
    public sealed class LegacyPdfPreferences
    {
        /// <summary>-1 = the file has no legacy keys.</summary>
        public int PdfPaperMode { get; set; } = -1;

        public string PdfPlotDeviceName { get; set; } = string.Empty;

        public int PdfIsoFullBleedSize { get; set; } = (int)IsoFullBleedSize.A4;

        public string PdfSelectedMediaName { get; set; } = string.Empty;

        public int PdfPlotArea { get; set; } = (int)PlotAreaMode.Extents;

        public bool PdfForcePlotPreset { get; set; } = true;

        public bool PdfFitToPaper { get; set; }

        public bool PdfCenterPlot { get; set; } = true;

        public bool PdfUseCustomScale { get; set; }

        public double PdfCustomScaleNumerator { get; set; } = 1.0;

        public double PdfCustomScaleDenominator { get; set; } = 1.0;

        public bool PdfScaleLineweights { get; set; }

        public int PdfPlotOrientation { get; set; } = (int)PlotDrawingOrientation.Landscape;

        public bool PdfPlotTransparency { get; set; } = true;

        public bool PdfPlotObjectLineweights { get; set; } = true;

        public bool PdfPlotWithPlotStyles { get; set; } = true;

        public bool PdfPlotPaperspaceLast { get; set; } = true;

        public bool PdfCombineIntoSinglePdf { get; set; }

        public string PdfPlotStyleTable { get; set; } = string.Empty;

        public bool HasValues => PdfPaperMode >= 0;

        /// <summary>
        /// The plot settings to start the window with: the old PDF preferences when the window file
        /// still has them, otherwise what was saved under <paramref name="plotKey"/> (or the preset).
        /// </summary>
        public static PlotExportSettings LoadPlotSettings(ISettingsStore store, string windowKey, string plotKey)
        {
            LegacyPdfPreferences legacy = store.Load<LegacyPdfPreferences>(windowKey);
            return legacy.HasValues ? legacy.ToPlotSettings() : store.Load<PlotExportSettings>(plotKey);
        }

        public PlotExportSettings ToPlotSettings()
        {
            var settings = new PlotExportSettings
            {
                PaperMode = PdfPaperMode <= (int)PlotPaperMode.SelectFromDevice ? (PlotPaperMode)PdfPaperMode : PlotPaperMode.UseLayout,
                IsoFullBleedSize = PdfIsoFullBleedSize >= 0 && PdfIsoFullBleedSize <= (int)IsoFullBleedSize.A4
                    ? (IsoFullBleedSize)PdfIsoFullBleedSize
                    : IsoFullBleedSize.A4,
                SelectedCanonicalMediaName = PdfSelectedMediaName ?? string.Empty,
                PlotArea = PdfPlotArea >= 0 && PdfPlotArea <= (int)PlotAreaMode.Layout ? (PlotAreaMode)PdfPlotArea : PlotAreaMode.Extents,
                ScaleLineweights = PdfScaleLineweights,
                // El "Horizontal" antiguo era rotación 0 para todo; "según cada presentación" da lo
                // mismo en los layouts habituales y además respeta los verticales.
                Orientation = PdfPlotOrientation == (int)PlotDrawingOrientation.Portrait
                    ? PlotDrawingOrientation.Portrait
                    : PlotDrawingOrientation.FromLayout,
                PlotTransparency = PdfPlotTransparency,
                PlotObjectLineweights = PdfPlotObjectLineweights,
                PlotWithPlotStyles = PdfPlotWithPlotStyles,
                PlotPaperspaceLast = PdfPlotPaperspaceLast,
                CombineIntoSingleFile = PdfCombineIntoSinglePdf,
                PlotStyleTableOverride = PdfPlotStyleTable ?? string.Empty
            };

            if (!string.IsNullOrWhiteSpace(PdfPlotDeviceName))
                settings.PlotDeviceName = PdfPlotDeviceName;

            // El "Preset GVR" antiguo era 1:1 + centrado; sin él, ajustar / escala propia / centrado.
            if (!PdfForcePlotPreset)
            {
                settings.FitToPaper = PdfFitToPaper;
                settings.CenterPlot = PdfCenterPlot;

                if (!PdfFitToPaper && PdfUseCustomScale && PdfCustomScaleNumerator > 0 && PdfCustomScaleDenominator > 0)
                {
                    settings.ScalePaperUnits = PdfCustomScaleNumerator;
                    settings.ScaleDrawingUnits = PdfCustomScaleDenominator;
                }
            }

            return settings;
        }
    }
}
