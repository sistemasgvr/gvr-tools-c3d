using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using GvrTools.Core.IO;
using GvrTools.Core.Text;

namespace GvrTools.Civil3D.Export.Plotting
{
    /// <summary>
    /// A named page setup (Page Setup Manager entry) already translated into plot-panel values.
    /// Choosing one fills the panel, exactly like picking it in the Plot dialog's "Page setup" combo.
    /// </summary>
    public sealed class NamedPageSetup
    {
        public NamedPageSetup(string name, string sourceLabel, PlotExportSettings settings)
        {
            Name = name ?? string.Empty;
            SourceLabel = sourceLabel ?? string.Empty;
            Settings = settings;
        }

        public string Name { get; }

        /// <summary>Where it came from: empty for the active drawing, the file name for an imported DWT/DWG.</summary>
        public string SourceLabel { get; }

        public PlotExportSettings Settings { get; }
    }

    /// <summary>
    /// Reads named page setups (the <c>PlotSettings</c> dictionary) from the active drawing or from
    /// any DWT/DWG on disk — the same source as the Page Setup Manager's "Import..." (PSETUPIN).
    /// Only paper-space setups are returned: model-space ones cannot be applied to layouts.
    /// </summary>
    public static class PageSetupRepository
    {
        private const double MillimetersPerInch = 25.4;

        public static IReadOnlyList<NamedPageSetup> Read(Database database, string sourceLabel = "")
        {
            var result = new List<NamedPageSetup>();
            if (database == null) return result;

            using (var tr = database.TransactionManager.StartOpenCloseTransaction())
            {
                var dictionary = (DBDictionary)tr.GetObject(database.PlotSettingsDictionaryId, OpenMode.ForRead);

                foreach (DBDictionaryEntry entry in dictionary)
                {
                    if (!(tr.GetObject(entry.Value, OpenMode.ForRead) is PlotSettings plotSettings)) continue;
                    if (plotSettings.ModelType) continue;

                    result.Add(new NamedPageSetup(entry.Key, sourceLabel, ToExportSettings(plotSettings)));
                }

                tr.Commit();
            }

            result.Sort((a, b) => NaturalSortComparer.Instance.Compare(a.Name, b.Name));
            return result;
        }

        /// <summary>
        /// Opens <paramref name="path"/> read-only (without showing it) and reads its page setups.
        /// </summary>
        /// <exception cref="PlotDeviceSetupException">The file cannot be read; message is user-facing.</exception>
        public static IReadOnlyList<NamedPageSetup> ReadFromFile(string path)
        {
            try
            {
                using (var database = new Database(false, true))
                {
                    database.ReadDwgFile(path, FileOpenMode.OpenForReadAndAllShare, true, string.Empty);
                    database.CloseInput(true);
                    return Read(database, Path.GetFileName(path));
                }
            }
            catch (Exception ex)
            {
                throw new PlotDeviceSetupException(
                    $"No se pudieron leer las configuraciones de página de \"{Path.GetFileName(path)}\": {ex.Message}", ex);
            }
        }

        /// <summary>Translates AutoCAD plot settings into the values the plot panel edits.</summary>
        public static PlotExportSettings ToExportSettings(PlotSettings plotSettings)
        {
            var settings = new PlotExportSettings
            {
                Source = PlotConfigSource.Manual,
                PlotDeviceName = plotSettings.PlotConfigurationName ?? string.Empty,
                PaperMode = PlotPaperMode.SelectFromDevice,
                SelectedCanonicalMediaName = plotSettings.CanonicalMediaName ?? string.Empty,
                PaperUnits = MapUnits(plotSettings.PlotPaperUnits),
                ScaleLineweights = plotSettings.ScaleLineweights,
                CenterPlot = plotSettings.PlotCentered,
                PlotStyleTableOverride = plotSettings.CurrentStyleSheet ?? string.Empty,
                PlotObjectLineweights = plotSettings.PrintLineweights,
                PlotTransparency = plotSettings.PlotTransparency,
                PlotWithPlotStyles = plotSettings.PlotPlotStyles,
                PlotPaperspaceLast = plotSettings.DrawViewportsFirst,
                HidePaperspaceObjects = plotSettings.PlotHidden,
                ShadeQuality = (PlotShadeQuality)(int)plotSettings.ShadePlotResLevel,
                ShadeCustomDpi = plotSettings.ShadePlotCustomDpi > 0 ? plotSettings.ShadePlotCustomDpi : 300
            };

            ReadPlotArea(plotSettings, settings);
            ReadScale(plotSettings, settings);
            ReadOffset(plotSettings, settings);

            PlotOrientationMath.FromQuarterTurns(
                (int)plotSettings.PlotRotation,
                PlotInfoBuilder.IsMediaLandscape(plotSettings),
                out bool landscape,
                out bool upsideDown);
            settings.Orientation = landscape ? PlotDrawingOrientation.Landscape : PlotDrawingOrientation.Portrait;
            settings.PlotUpsideDown = upsideDown;

            return settings;
        }

        private static void ReadPlotArea(PlotSettings plotSettings, PlotExportSettings settings)
        {
            switch (plotSettings.PlotType)
            {
                case Autodesk.AutoCAD.DatabaseServices.PlotType.Display:
                    settings.PlotArea = PlotAreaMode.Display;
                    break;
                case Autodesk.AutoCAD.DatabaseServices.PlotType.Layout:
                    settings.PlotArea = PlotAreaMode.Layout;
                    break;
                case Autodesk.AutoCAD.DatabaseServices.PlotType.Window:
                    Extents2d window = plotSettings.PlotWindowArea;
                    settings.PlotArea = PlotAreaMode.Window;
                    settings.UseCommonWindow = true;
                    settings.WindowMinX = window.MinPoint.X;
                    settings.WindowMinY = window.MinPoint.Y;
                    settings.WindowMaxX = window.MaxPoint.X;
                    settings.WindowMaxY = window.MaxPoint.Y;
                    break;
                default:
                    // Vista / Límites no aplican a un lote de presentaciones: Extensión es lo más cercano.
                    settings.PlotArea = PlotAreaMode.Extents;
                    break;
            }
        }

        private static void ReadScale(PlotSettings plotSettings, PlotExportSettings settings)
        {
            if (plotSettings.UseStandardScale)
            {
                if (plotSettings.StdScaleType == StdScaleType.ScaleToFit)
                {
                    settings.FitToPaper = true;
                    return;
                }

                // StdScale es papel/dibujo (1:100 → 0.01).
                double ratio = plotSettings.StdScale;
                settings.ScalePaperUnits = ratio >= 1 ? ratio : 1;
                settings.ScaleDrawingUnits = ratio >= 1 ? 1 : (ratio > 0 ? 1 / ratio : 1);
                return;
            }

            CustomScale custom = plotSettings.CustomPrintScale;
            settings.ScalePaperUnits = custom.Numerator > 0 ? custom.Numerator : 1;
            settings.ScaleDrawingUnits = custom.Denominator > 0 ? custom.Denominator : 1;
        }

        /// <summary>The plot origin is stored in millimetres; the panel shows it in paper units.</summary>
        private static void ReadOffset(PlotSettings plotSettings, PlotExportSettings settings)
        {
            Point2d origin = plotSettings.PlotOrigin;
            double factor = settings.PaperUnits == PlotPaperUnits.Inches ? 1 / MillimetersPerInch : 1.0;
            settings.OffsetX = origin.X * factor;
            settings.OffsetY = origin.Y * factor;
        }

        private static PlotPaperUnits MapUnits(PlotPaperUnit units)
        {
            switch (units)
            {
                case PlotPaperUnit.Inches: return PlotPaperUnits.Inches;
                case PlotPaperUnit.Pixels: return PlotPaperUnits.Pixels;
                default: return PlotPaperUnits.Millimeters;
            }
        }
    }
}
