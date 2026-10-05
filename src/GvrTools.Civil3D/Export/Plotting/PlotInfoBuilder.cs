using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.PlottingServices;
using GvrTools.Core.Diagnostics;
using GvrTools.Core.IO;
using PlotType = Autodesk.AutoCAD.DatabaseServices.PlotType;

namespace GvrTools.Civil3D.Export.Plotting
{
    /// <summary>Where one layout's plot goes, as resolved by <see cref="PlotInfoBuilder"/>.</summary>
    public sealed class PlotTarget
    {
        public PlotTarget(string deviceName, PlotOutputKind output, string extension)
        {
            DeviceName = deviceName;
            Output = output;
            Extension = PlotOutput.NormalizeExtension(extension);
        }

        public string DeviceName { get; }

        public PlotOutputKind Output { get; }

        public bool WritesFile => PlotOutput.IsFileOutput(Output);

        /// <summary>".pdf", ".dwf", ".png"... Only meaningful when <see cref="WritesFile"/>.</summary>
        public string Extension { get; }
    }

    /// <summary>
    /// Turns <see cref="PlotExportSettings"/> into a validated <see cref="PlotInfo"/> for one layout.
    /// Shared by the batch engine and the plot preview, so the preview shows exactly what the batch
    /// will plot.
    ///
    /// Order follows the Plot dialog and the DevGuide samples: device → media → paper units → plot
    /// area → scale → rotation → center/offset → flags → plot style table. Each step that AutoCAD
    /// rejects becomes a <see cref="PlotDeviceSetupException"/> with a user-facing message, which
    /// costs that layout only, never the rest of the batch.
    /// </summary>
    public sealed class PlotInfoBuilder
    {
        private const double MillimetersPerInch = 25.4;

        private readonly PlotExportSettings _settings;
        private readonly ILog _log;

        public PlotInfoBuilder(PlotExportSettings settings, ILog log)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _log = log ?? NullLog.Instance;
        }

        /// <exception cref="PlotDeviceSetupException">The layout cannot be plotted with these settings.</exception>
        public PlotTarget Build(PlotInfo plotInfo, Layout layout)
        {
            plotInfo.Layout = layout.Id;

            var plotSettings = new PlotSettings(layout.ModelType);
            plotSettings.CopyFrom(layout);

            PlotSettingsValidator validator = PlotSettingsValidator.Current;
            string layoutName = layout.LayoutName;

            string device = ResolveDevice(plotSettings, layoutName);
            PlotDeviceDetails details = PlotDeviceRepository.GetDeviceDetails(device);
            if (details == null)
            {
                throw new PlotDeviceSetupException(
                    $"AutoCAD no pudo cargar el dispositivo \"{device}\" (presentación '{layoutName}'). " +
                    "Comprueba que funcione en el cuadro Trazar de Civil 3D.");
            }

            if (_settings.Source == PlotConfigSource.Manual)
            {
                // La orientación propia se lee ANTES de cambiar el papel: depende de cómo está
                // definido el papel original de la presentación.
                ResolveOrientation(plotSettings, out bool landscape, out bool upsideDown);

                ApplyDeviceAndMedia(validator, plotSettings, device, layoutName, landscape);
                ApplyPaperUnits(validator, plotSettings, details);
                ApplyPlotArea(validator, plotSettings, layoutName);
                ApplyScale(validator, plotSettings);
                ApplyRotation(validator, plotSettings, landscape, upsideDown);
                ApplyCenterAndOffset(validator, plotSettings);
                ApplyPlotOptionFlags(plotSettings);
                ApplyShadedViewports(plotSettings, details);
            }

            ApplyPlotStyleTable(validator, plotSettings, layout);

            plotInfo.OverrideSettings = plotSettings;

            try
            {
                var validity = new PlotInfoValidator { MediaMatchingPolicy = MatchingPolicy.MatchEnabled };
                validity.Validate(plotInfo);
            }
            catch (Exception ex)
            {
                throw new PlotDeviceSetupException(
                    $"La validación del trazado falló para '{layoutName}' (dispositivo \"{device}\"): {ex.Message}", ex);
            }

            return new PlotTarget(device, details.ResolveOutput(_settings.PlotToFile), details.DefaultFileExtension);
        }

        // ---------------------------------------------------------------- device and paper

        private string ResolveDevice(PlotSettings plotSettings, string layoutName)
        {
            if (_settings.Source == PlotConfigSource.LayoutOwn)
            {
                string own = plotSettings.PlotConfigurationName;
                if (PlotDeviceRepository.IsNoneDevice(own))
                {
                    throw new PlotDeviceSetupException(
                        $"La presentación '{layoutName}' no tiene impresora asignada (\"Ninguno\"). " +
                        "Asígnale una en su configuración de página o usa la configuración manual.");
                }

                if (!PlotDeviceRepository.IsInstalled(own))
                {
                    throw new PlotDeviceSetupException(
                        $"La presentación '{layoutName}' usa el dispositivo \"{own}\", que no está instalado en este equipo.");
                }

                return own;
            }

            string device = string.IsNullOrWhiteSpace(_settings.PlotDeviceName)
                ? PlotDeviceRepository.DefaultPdfDeviceName
                : _settings.PlotDeviceName.Trim();

            if (!PlotDeviceRepository.IsInstalled(device))
                throw new PlotDeviceSetupException($"El dispositivo \"{device}\" no está instalado en este equipo.");

            return device;
        }

        /// <summary>
        /// Points the plot settings at <paramref name="deviceName"/> and picks the paper. Without a
        /// canonical media name that belongs to the device, <see cref="PlotInfoValidator"/> fails
        /// with <c>eNoMatchingMedia</c>.
        /// </summary>
        /// <summary>
        /// The orientation to plot with: the panel's choice, or with "per layout" the one the
        /// layout's own page setup produces (rotation read against its own paper).
        /// </summary>
        private void ResolveOrientation(PlotSettings layoutSettings, out bool landscape, out bool upsideDown)
        {
            if (_settings.Orientation == PlotDrawingOrientation.FromLayout)
            {
                PlotOrientationMath.FromQuarterTurns(
                    (int)layoutSettings.PlotRotation,
                    IsMediaLandscape(layoutSettings),
                    out landscape,
                    out upsideDown);
                return;
            }

            landscape = _settings.Orientation == PlotDrawingOrientation.Landscape;
            upsideDown = _settings.PlotUpsideDown;
        }

        private void ApplyDeviceAndMedia(PlotSettingsValidator validator, PlotSettings settings, string deviceName, string layoutName, bool landscape)
        {
            try
            {
                string layoutMedia = settings.CanonicalMediaName;

                validator.SetPlotConfigurationName(settings, deviceName, null);
                validator.RefreshLists(settings);

                StringCollection mediaCollection = validator.GetCanonicalMediaNameList(settings);
                if (mediaCollection == null || mediaCollection.Count == 0) return;

                var mediaList = new List<string>(mediaCollection.Count);
                foreach (string m in mediaCollection) mediaList.Add(m);

                string media;
                switch (_settings.PaperMode)
                {
                    case PlotPaperMode.ForceIsoFullBleed:
                    {
                        string letter = _settings.IsoFullBleedSize.ToString();
                        media = IsoFullBleedMediaPicker.Pick(letter, mediaList, preferLandscape: landscape);
                        if (media == null)
                        {
                            throw new PlotDeviceSetupException(
                                $"El dispositivo \"{deviceName}\" no ofrece ISO full bleed {letter} " +
                                $"(presentación '{layoutName}'). Elige otro papel o usa el tamaño de cada presentación.");
                        }

                        break;
                    }

                    case PlotPaperMode.SelectFromDevice:
                    {
                        string wanted = (_settings.SelectedCanonicalMediaName ?? string.Empty).Trim();
                        if (wanted.Length == 0)
                            throw new PlotDeviceSetupException("Selecciona un tamaño de papel de la lista del dispositivo.");

                        media = PlotMediaResolver.FindExact(wanted, mediaList);
                        if (media == null)
                        {
                            throw new PlotDeviceSetupException(
                                $"El papel \"{PlotMediaNames.Humanize(wanted)}\" no está disponible en \"{deviceName}\" " +
                                $"(presentación '{layoutName}'). Elige otro tamaño o cambia de dispositivo.");
                        }

                        break;
                    }

                    default:
                    {
                        string deviceDefault = PlotDeviceRepository.GetMedia(deviceName).DefaultCanonicalName;
                        media = PlotMediaResolver.Resolve(layoutMedia, mediaList, deviceDefault);
                        if (media == null) return;

                        if (!string.IsNullOrWhiteSpace(layoutMedia) &&
                            !string.Equals(media, layoutMedia, StringComparison.OrdinalIgnoreCase))
                        {
                            _log.Warn(
                                $"La presentación '{layoutName}' pide el papel '{layoutMedia}', " +
                                $"pero el dispositivo \"{deviceName}\" no lo ofrece; se usará '{media}'.");
                        }

                        break;
                    }
                }

                if (!string.Equals(settings.CanonicalMediaName, media, StringComparison.OrdinalIgnoreCase))
                    validator.SetCanonicalMediaName(settings, media);
            }
            catch (PlotDeviceSetupException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new PlotDeviceSetupException(
                    $"No se pudo configurar el dispositivo de trazado \"{deviceName}\": {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Raster devices (PublishToWeb PNG/JPG) define their paper in pixels and AutoCAD already set
        /// that with the media; everything else follows the chosen mm/inches.
        /// </summary>
        private void ApplyPaperUnits(PlotSettingsValidator validator, PlotSettings settings, PlotDeviceDetails details)
        {
            if (PlotOutput.UsesPixels(PlotOutput.Classify(details.DefaultFileExtension, plotsToFile: true)))
                return;

            PlotPaperUnit wanted = _settings.PaperUnits == PlotPaperUnits.Inches
                ? PlotPaperUnit.Inches
                : PlotPaperUnit.Millimeters;

            try
            {
                if (settings.PlotPaperUnits != wanted)
                    validator.SetPlotPaperUnits(settings, wanted);
            }
            catch (Exception ex)
            {
                _log.Warn("No se pudieron aplicar las unidades de papel: " + ex.Message);
            }
        }

        // ---------------------------------------------------------------- area, scale, rotation, offset

        private void ApplyPlotArea(PlotSettingsValidator validator, PlotSettings settings, string layoutName)
        {
            PlotType plotType = MapPlotArea(_settings.PlotArea);

            try
            {
                if (plotType == PlotType.Window)
                {
                    if (_settings.UseCommonWindow)
                    {
                        double minX = Math.Min(_settings.WindowMinX, _settings.WindowMaxX);
                        double minY = Math.Min(_settings.WindowMinY, _settings.WindowMaxY);
                        double maxX = Math.Max(_settings.WindowMinX, _settings.WindowMaxX);
                        double maxY = Math.Max(_settings.WindowMinY, _settings.WindowMaxY);

                        if (maxX - minX <= 1e-9 || maxY - minY <= 1e-9)
                        {
                            throw new PlotDeviceSetupException(
                                "La ventana de trazado común no tiene área. Desígnala de nuevo o escribe sus esquinas.");
                        }

                        validator.SetPlotWindowArea(settings, new Extents2d(minX, minY, maxX, maxY));
                    }
                    else if (!HasUsablePlotWindow(settings))
                    {
                        throw new PlotDeviceSetupException(
                            $"La presentación '{layoutName}' no tiene una ventana de trazado guardada. " +
                            "Usa una ventana común, o elige Extensión / Pantalla / Presentación.");
                    }
                }

                validator.SetPlotType(settings, plotType);
            }
            catch (PlotDeviceSetupException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new PlotDeviceSetupException(
                    $"No se pudo establecer el área de trazado ({_settings.PlotArea}) en '{layoutName}': {ex.Message}", ex);
            }
        }

        private static bool HasUsablePlotWindow(PlotSettings settings)
        {
            try
            {
                Extents2d window = settings.PlotWindowArea;
                return Math.Abs(window.MaxPoint.X - window.MinPoint.X) > 1e-9 &&
                       Math.Abs(window.MaxPoint.Y - window.MinPoint.Y) > 1e-9;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static PlotType MapPlotArea(PlotAreaMode area)
        {
            switch (area)
            {
                case PlotAreaMode.Window: return PlotType.Window;
                case PlotAreaMode.Display: return PlotType.Display;
                case PlotAreaMode.Layout: return PlotType.Layout;
                default: return PlotType.Extents;
            }
        }

        private void ApplyScale(PlotSettingsValidator validator, PlotSettings settings)
        {
            try
            {
                if (_settings.FitToPaper)
                {
                    validator.SetUseStandardScale(settings, true);
                    validator.SetStdScaleType(settings, StdScaleType.ScaleToFit);
                }
                else
                {
                    double paper = _settings.ScalePaperUnits;
                    double drawing = _settings.ScaleDrawingUnits;
                    if (paper <= 0 || drawing <= 0)
                    {
                        throw new PlotDeviceSetupException(
                            "La escala de trazado debe usar valores mayores que cero (p. ej. 1 mm = 100 unidades).");
                    }

                    if (Math.Abs(paper - drawing) <= 1e-12 * Math.Max(paper, drawing))
                    {
                        validator.SetUseStandardScale(settings, true);
                        validator.SetStdScaleType(settings, StdScaleType.StdScale1To1);
                    }
                    else
                    {
                        // DevGuide / Managed Reference: el nombre correcto es SetCustomPrintScale.
                        validator.SetUseStandardScale(settings, false);
                        validator.SetCustomPrintScale(settings, new CustomScale(paper, drawing));
                    }
                }

                settings.ScaleLineweights = _settings.ScaleLineweights;
            }
            catch (PlotDeviceSetupException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new PlotDeviceSetupException("No se pudo aplicar la escala del trazado: " + ex.Message, ex);
            }
        }

        /// <summary>
        /// Vertical/Horizontal + upside-down → 0/90/180/270°, relative to how the device defines the
        /// chosen paper (see <see cref="PlotOrientationMath"/>).
        /// </summary>
        private static void ApplyRotation(PlotSettingsValidator validator, PlotSettings settings, bool landscape, bool upsideDown)
        {
            try
            {
                int turns = PlotOrientationMath.ToQuarterTurns(landscape, upsideDown, IsMediaLandscape(settings));

                validator.SetPlotRotation(settings, (PlotRotation)turns);
            }
            catch (Exception ex)
            {
                throw new PlotDeviceSetupException("No se pudo aplicar la orientación del trazado: " + ex.Message, ex);
            }
        }

        /// <summary>Whether the current media is defined wider than tall by the device.</summary>
        public static bool IsMediaLandscape(PlotSettings settings)
        {
            if (PlotMediaNames.TryGetDimensions(settings.CanonicalMediaName, out double width, out double height))
                return width > height;

            try
            {
                Point2d size = settings.PlotPaperSize;
                return size.X > size.Y;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void ApplyCenterAndOffset(PlotSettingsValidator validator, PlotSettings settings)
        {
            // Con área "Presentación" AutoCAD no permite centrar: el trazado sale desde el origen de
            // la hoja, igual que en el cuadro Trazar (la casilla queda deshabilitada).
            if (_settings.PlotArea == PlotAreaMode.Layout)
            {
                ApplyOffset(validator, settings);
                return;
            }

            try
            {
                validator.SetPlotCentered(settings, _settings.CenterPlot);
            }
            catch (Exception ex)
            {
                throw new PlotDeviceSetupException("No se pudo centrar el trazado: " + ex.Message, ex);
            }

            if (!_settings.CenterPlot)
                ApplyOffset(validator, settings);
        }

        /// <summary>The plot origin is stored in millimetres whatever the paper units (DXF 46/47).</summary>
        private void ApplyOffset(PlotSettingsValidator validator, PlotSettings settings)
        {
            if (settings.PlotPaperUnits == PlotPaperUnit.Pixels) return;

            double factor = _settings.PaperUnits == PlotPaperUnits.Inches ? MillimetersPerInch : 1.0;

            try
            {
                validator.SetPlotOrigin(settings, new Point2d(_settings.OffsetX * factor, _settings.OffsetY * factor));
            }
            catch (Exception ex)
            {
                throw new PlotDeviceSetupException("No se pudo aplicar el desfase del trazado: " + ex.Message, ex);
            }
        }

        // ---------------------------------------------------------------- flags, shading, styles

        private void ApplyPlotOptionFlags(PlotSettings settings)
        {
            try { settings.PrintLineweights = _settings.PlotObjectLineweights; }
            catch (Exception ex) { _log.Warn("No se pudo aplicar PrintLineweights: " + ex.Message); }

            try { settings.PlotTransparency = _settings.PlotTransparency; }
            catch (Exception ex) { _log.Warn("No se pudo aplicar PlotTransparency: " + ex.Message); }

            // “Plot paperspace last” ↔ DrawViewportsFirst (DevGuide sample).
            try { settings.DrawViewportsFirst = _settings.PlotPaperspaceLast; }
            catch (Exception ex) { _log.Warn("No se pudo aplicar DrawViewportsFirst: " + ex.Message); }

            try { settings.PlotHidden = _settings.HidePaperspaceObjects; }
            catch (Exception ex) { _log.Warn("No se pudo aplicar PlotHidden: " + ex.Message); }

            try { settings.PlotPlotStyles = _settings.PlotWithPlotStyles; }
            catch (Exception ex)
            {
                throw new PlotDeviceSetupException(
                    "No se pudo configurar el uso de estilos de trazado (PlotPlotStyles): " + ex.Message, ex);
            }
        }

        /// <summary>
        /// Quality / DPI of shaded and rendered viewports. The shade mode itself belongs to each
        /// viewport in paper space (the Plot dialog greys it out for layouts), so only the
        /// resolution is applied here.
        /// </summary>
        private void ApplyShadedViewports(PlotSettings settings, PlotDeviceDetails details)
        {
            try
            {
                settings.ShadePlotResLevel = (ShadePlotResLevel)(int)_settings.ShadeQuality;

                if (_settings.ShadeQuality == PlotShadeQuality.Custom)
                {
                    int max = details.MaximumDpi > 0 ? details.MaximumDpi : short.MaxValue;
                    int dpi = Math.Max(100, Math.Min(_settings.ShadeCustomDpi, Math.Min(max, short.MaxValue)));
                    settings.ShadePlotCustomDpi = (short)dpi;
                }
            }
            catch (Exception ex)
            {
                _log.Warn("No se pudo aplicar la calidad de las ventanas sombreadas: " + ex.Message);
            }
        }

        /// <summary>
        /// Applies the pen assignments (.ctb/.stb) the plot should use. When an override was
        /// requested and AutoCAD rejects it, the layout fails (hard).
        /// </summary>
        private void ApplyPlotStyleTable(PlotSettingsValidator validator, PlotSettings settings, Layout layout)
        {
            string requested = _settings.PlotStyleTableOverride;

            if (string.IsNullOrWhiteSpace(requested))
            {
                string own = SafeCurrentStyleSheet(layout);
                if (!string.IsNullOrWhiteSpace(own) && !PlotStyleTableRepository.IsInstalled(own))
                {
                    _log.Warn($"La presentación '{layout.LayoutName}' usa la tabla de estilos '{own}', " +
                              "que no está instalada; el trazado puede salir sin las plumas correctas.");
                }

                return;
            }

            try
            {
                validator.SetCurrentStyleSheet(settings, requested);
            }
            catch (Exception ex)
            {
                throw new PlotDeviceSetupException(
                    $"No se pudo aplicar la tabla de estilos '{requested}' en '{layout.LayoutName}': {ex.Message}", ex);
            }
        }

        private static string SafeCurrentStyleSheet(Layout layout)
        {
            try { return layout.CurrentStyleSheet; }
            catch (Exception) { return null; }
        }
    }
}
