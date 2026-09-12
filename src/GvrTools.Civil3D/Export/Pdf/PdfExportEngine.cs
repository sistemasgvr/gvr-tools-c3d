using System;
using System.IO;
using System.Threading;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.PlottingServices;
using PlotType = Autodesk.AutoCAD.DatabaseServices.PlotType;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using System.Collections.Generic;
using GvrTools.Civil3D.Layouts;
using GvrTools.Civil3D.Model;
using GvrTools.Core.Batch;
using GvrTools.Core.Diagnostics;
using GvrTools.Core.IO;
using GvrTools.Core.Naming;

namespace GvrTools.Civil3D.Export.Pdf
{
    /// <summary>
    /// Exports PDFs with AutoCAD's own plot engine (<see cref="PlotEngine"/> +
    /// <see cref="PlotSettings"/>/<see cref="PlotSettingsValidator"/>), driving the "DWG To PDF.pc3"
    /// device that ships with every AutoCAD-based product.
    ///
    /// Two modes:
    ///   * one PDF per layout (default), and
    ///   * one multi-page PDF for the whole selection (<see cref="PdfExportSettings.CombineIntoSinglePdf"/>),
    ///     using AutoCAD's multi-sheet plot pipeline (a single BeginDocument, one page per layout).
    ///
    /// AutoCAD refuses to plot a layout that is not the current one (<c>eLayoutNotCurrent</c>), so
    /// each layout is made current — with the document locked, since the run happens from a modeless
    /// window callback, not a command — right before it is plotted.
    /// </summary>
    public sealed class PdfExportEngine : IExportEngine
    {
        public ExportFormat Format => ExportFormat.Pdf;

        public string StrategyDescription =>
            "Motor de trazado nativo de AutoCAD: PC3 y CTB forzados, Extents 1:1 centrado, papel del layout.";

        public IExportSession BeginSession(ExportRequest request, int totalItems)
        {
            PdfExportSettings settings = request.SettingsAs<PdfExportSettings>();

            if (PlotFactory.ProcessPlotState != ProcessPlotState.NotPlotting)
                throw new ExportSetupException("Ya hay un trazado en curso en AutoCAD. Espera a que termine e inténtalo de nuevo.");

            Document document = request.Document ?? AcApp.DocumentManager.MdiActiveDocument;
            if (document == null)
                throw new ExportSetupException("No hay un dibujo activo para exportar.");

            return new Session(request, settings, document, totalItems);
        }

        private sealed class Session : IExportSession
        {
            private readonly Database _database;
            private readonly Document _document;
            private readonly ExportFileNamer _namer;
            private readonly PdfExportSettings _settings;
            private readonly string _folder;
            private readonly ILog _log;
            private readonly object _previousBackgroundPlot;
            private readonly string _originalLayout;
            private readonly int _totalItems;

            // Combined (multi-sheet) mode state, only used when _settings.CombineIntoSinglePdf is true.
            private readonly bool _combine;
            private readonly string _combinedPath;
            private PlotEngine _combinedEngine;
            private bool _documentStarted;
            private int _pageIndex;

            internal Session(ExportRequest request, PdfExportSettings settings, Document document, int totalItems)
            {
                _database = request.Database;
                _document = document;
                _settings = settings;
                _folder = request.DestinationFolder;
                _log = request.Log;
                _totalItems = totalItems < 1 ? 1 : totalItems;
                _namer = new ExportFileNamer(
                    request.DestinationFolder,
                    request.NamingPattern,
                    ExportFormatInfo.Extension(ExportFormat.Pdf),
                    request.Drawing.ToTokens());

                _combine = settings.CombineIntoSinglePdf;
                if (_combine)
                    _combinedPath = BuildCombinedPath(request);

                try { _originalLayout = LayoutManager.Current.CurrentLayout; }
                catch (Exception) { _originalLayout = null; }

                // El motor de publicación de AutoCAD escribe el PDF en un proceso EN SEGUNDO PLANO
                // cuando BACKGROUNDPLOT está activo (valor por defecto en muchas instalaciones), así
                // que el archivo aparece DESPUÉS de que EndPlot retorna y File.Exists daría un falso
                // negativo aunque el PDF sí se cree. Forzamos primer plano durante la exportación.
                _previousBackgroundPlot = TrySetForegroundPlot();
            }

            public BatchItemResult Export(LayoutSnapshot layout)
            {
                return _combine ? ExportCombinedPage(layout) : ExportSingleFile(layout);
            }

            // ---------------------------------------------------------------- one PDF per layout

            private BatchItemResult ExportSingleFile(LayoutSnapshot layout)
            {
                using (_document.LockDocument())
                using (var tr = _database.TransactionManager.StartTransaction())
                {
                    Layout liveLayout = LayoutRepository.Resolve(_database, tr, layout);
                    if (liveLayout == null)
                        return BatchItemResult.Failure(layout.Label, "La presentación ya no existe en el dibujo.");

                    SetCurrentLayout(liveLayout);

                    string baseName = _namer.ReserveBaseName(layout);
                    string outputPath = Path.Combine(_folder, baseName + ".pdf");

                    using (var plotInfo = new PlotInfo())
                    {
                        try
                        {
                            BuildPlotInfo(plotInfo, liveLayout);
                        }
                        catch (PlotDeviceSetupException ex)
                        {
                            // Configuración de página inservible en ESTA presentación: cuesta la
                            // presentación, no el resto del lote.
                            _log.Warn($"No se pudo preparar el trazado de '{layout.Label}': {ex.Message}");
                            return BatchItemResult.Failure(layout.Label, ex.Message);
                        }

                        using (var engine = PlotFactory.CreatePublishEngine())
                        {
                            engine.BeginPlot(null, null);
                            engine.BeginDocument(
                                plotInfo,
                                Path.GetFileNameWithoutExtension(outputPath),
                                null,
                                1,
                                true,
                                outputPath);

                            PlotOnePage(engine, plotInfo, isLastPage: true);

                            engine.EndDocument(null);
                            engine.EndPlot(null);
                        }
                    }

                    tr.Commit();

                    if (!WaitForFile(outputPath, TimeSpan.FromSeconds(10)))
                    {
                        _log.Warn($"AutoCAD reportó éxito pero no se encontró '{outputPath}'.");
                        return BatchItemResult.Failure(layout.Label, "AutoCAD no generó el archivo PDF esperado.");
                    }

                    return BatchItemResult.Success(layout.Label, outputPath);
                }
            }

            // ---------------------------------------------------------------- one multi-page PDF

            private BatchItemResult ExportCombinedPage(LayoutSnapshot layout)
            {
                using (_document.LockDocument())
                using (var tr = _database.TransactionManager.StartTransaction())
                {
                    Layout liveLayout = LayoutRepository.Resolve(_database, tr, layout);
                    if (liveLayout == null)
                        return BatchItemResult.Failure(layout.Label, "La presentación ya no existe en el dibujo.");

                    SetCurrentLayout(liveLayout);

                    using (var plotInfo = new PlotInfo())
                    {
                        try
                        {
                            BuildPlotInfo(plotInfo, liveLayout);
                        }
                        catch (PlotDeviceSetupException ex)
                        {
                            _log.Warn($"No se pudo preparar el trazado de '{layout.Label}': {ex.Message}");
                            return BatchItemResult.Failure(layout.Label, ex.Message);
                        }

                        if (_combinedEngine == null)
                        {
                            _combinedEngine = PlotFactory.CreatePublishEngine();
                            _combinedEngine.BeginPlot(null, null);
                            _combinedEngine.BeginDocument(
                                plotInfo,
                                Path.GetFileNameWithoutExtension(_combinedPath),
                                null,
                                1,
                                true,
                                _combinedPath);
                            _documentStarted = true;
                        }

                        bool isLast = _pageIndex >= _totalItems - 1;
                        PlotOnePage(_combinedEngine, plotInfo, isLast);
                        _pageIndex++;
                    }

                    tr.Commit();
                }

                return BatchItemResult.Success(layout.Label, _combinedPath);
            }

            // ---------------------------------------------------------------- shared plumbing

            /// <summary>Makes <paramref name="layout"/> the current layout so AutoCAD will plot it.</summary>
            private void SetCurrentLayout(Layout layout)
            {
                LayoutManager lm = LayoutManager.Current;
                if (!string.Equals(lm.CurrentLayout, layout.LayoutName, StringComparison.Ordinal))
                    lm.CurrentLayout = layout.LayoutName;
            }

            private void BuildPlotInfo(PlotInfo plotInfo, Layout liveLayout)
            {
                plotInfo.Layout = liveLayout.Id;

                PlotSettings plotSettings = new PlotSettings(liveLayout.ModelType);
                plotSettings.CopyFrom(liveLayout);

                PlotSettingsValidator validator = PlotSettingsValidator.Current;

                // Por defecto se fuerza el PC3 elegido en la UI en TODAS las hojas (diferenciador vs
                // Batch Plot nativo con page setups). Solo se conserva el del layout si ForcePlotDevice
                // está off Y ese dispositivo ya es un PDF usable.
                string deviceToUse;
                if (_settings.ForcePlotDevice ||
                    !PlotDeviceRepository.IsUsablePdfDevice(plotSettings.PlotConfigurationName))
                {
                    deviceToUse = string.IsNullOrWhiteSpace(_settings.PlotDeviceName)
                        ? PlotDeviceRepository.DefaultPdfDeviceName
                        : _settings.PlotDeviceName.Trim();
                }
                else
                {
                    deviceToUse = plotSettings.PlotConfigurationName;
                }

                EnsureDeviceAndMedia(validator, plotSettings, deviceToUse, liveLayout.LayoutName);

                // Preset GVR: Extents + 1:1 + centrado (sin ScaleToFit / Fit to paper).
                // StdScaleType.StdScale1To1 — ver docs/PLOT_API_NOTES.md.
                if (_settings.ForcePlotPreset)
                {
                    validator.SetPlotType(plotSettings, PlotType.Extents);
                    validator.SetUseStandardScale(plotSettings, true);
                    validator.SetStdScaleType(plotSettings, StdScaleType.StdScale1To1);
                    validator.SetPlotCentered(plotSettings, true);
                }

                ApplyPlotStyleTable(validator, plotSettings, liveLayout);
                ApplyPlotTransparency(plotSettings);

                plotInfo.OverrideSettings = plotSettings;

                try
                {
                    var validity = new PlotInfoValidator { MediaMatchingPolicy = MatchingPolicy.MatchEnabled };
                    validity.Validate(plotInfo);
                }
                catch (Exception ex)
                {
                    throw new PlotDeviceSetupException(
                        $"La validación del trazado falló para '{liveLayout.LayoutName}' " +
                        $"(dispositivo \"{deviceToUse}\"): {ex.Message}", ex);
                }
            }

            /// <summary>
            /// Applies the pen assignments (.ctb/.stb) the PDF should be plotted with.
            ///
            /// Two things have to be right for a plot style table to actually reach the paper: the
            /// table has to be set, and <c>PlotPlotStyles</c> has to be on — a layout saved with pen
            /// assignments disabled ignores its own table, and the PDF comes out with raw object
            /// colours and lineweights.
            ///
            /// When the UI requested an override and AutoCAD rejects it, the layout fails (hard)
            /// instead of plotting with raw colours silently.
            /// </summary>
            private void ApplyPlotStyleTable(PlotSettingsValidator validator, PlotSettings plotSettings, Layout liveLayout)
            {
                string requested = _settings.PlotStyleTableOverride;
                bool isOverride = !string.IsNullOrWhiteSpace(requested);

                if (!isOverride)
                {
                    string own = SafeCurrentStyleSheet(liveLayout);
                    if (!string.IsNullOrWhiteSpace(own) && !PlotStyleTableRepository.IsInstalled(own))
                    {
                        _log.Warn($"La presentación '{liveLayout.LayoutName}' usa la tabla de estilos '{own}', " +
                                  "que no está instalada; el PDF puede salir sin las plumas correctas.");
                    }
                }
                else
                {
                    try
                    {
                        validator.SetCurrentStyleSheet(plotSettings, requested);
                    }
                    catch (Exception ex)
                    {
                        throw new PlotDeviceSetupException(
                            $"No se pudo aplicar la tabla de estilos '{requested}' en '{liveLayout.LayoutName}': {ex.Message}",
                            ex);
                    }
                }

                try { plotSettings.PlotPlotStyles = true; }
                catch (Exception ex)
                {
                    throw new PlotDeviceSetupException(
                        "No se pudo activar el uso de estilos de trazado (PlotPlotStyles): " + ex.Message, ex);
                }
            }

            private void ApplyPlotTransparency(PlotSettings plotSettings)
            {
                try
                {
                    plotSettings.PlotTransparency = _settings.PlotTransparency;
                }
                catch (Exception ex)
                {
                    _log.Warn("No se pudo aplicar PlotTransparency: " + ex.Message);
                }
            }

            private static string SafeCurrentStyleSheet(Layout layout)
            {
                try { return layout.CurrentStyleSheet; }
                catch (Exception) { return null; }
            }

            /// <summary>
            /// Emits one page. BeginPlot/BeginDocument must already have been called on the engine.
            /// PlotEngine's Begin*/End* pair follows the ADN-documented "headless plot" shape.
            /// </summary>
            private static void PlotOnePage(PlotEngine engine, PlotInfo plotInfo, bool isLastPage)
            {
                var pageInfo = new PlotPageInfo();
                engine.BeginPage(pageInfo, plotInfo, isLastPage, null);
                engine.BeginGenerateGraphics(null);
                engine.EndGenerateGraphics(null);
                engine.EndPage(null);
            }

            public void Dispose()
            {
                // Cierra el documento de trazado del PDF combinado (el archivo se escribe aquí).
                if (_combinedEngine != null)
                {
                    try
                    {
                        if (_documentStarted)
                        {
                            _combinedEngine.EndDocument(null);
                            _combinedEngine.EndPlot(null);
                        }
                    }
                    catch (Exception ex)
                    {
                        _log.Warn("No se pudo finalizar el PDF combinado: " + ex.Message);
                    }
                    finally
                    {
                        try { _combinedEngine.Dispose(); } catch (Exception) { }
                        _combinedEngine = null;
                    }

                    if (_documentStarted && !WaitForFile(_combinedPath, TimeSpan.FromSeconds(15)))
                        _log.Warn($"No se encontró el PDF combinado esperado '{_combinedPath}'.");
                }

                RestoreCurrentLayout();

                if (_previousBackgroundPlot != null)
                {
                    try
                    {
                        AcApp.SetSystemVariable("BACKGROUNDPLOT", _previousBackgroundPlot);
                    }
                    catch (Exception ex)
                    {
                        _log.Warn("No se pudo restaurar BACKGROUNDPLOT: " + ex.Message);
                    }
                }
            }

            private void RestoreCurrentLayout()
            {
                if (string.IsNullOrEmpty(_originalLayout)) return;

                try
                {
                    using (_document.LockDocument())
                    {
                        if (!string.Equals(LayoutManager.Current.CurrentLayout, _originalLayout, StringComparison.Ordinal))
                            LayoutManager.Current.CurrentLayout = _originalLayout;
                    }
                }
                catch (Exception ex)
                {
                    _log.Warn("No se pudo restaurar la presentación activa: " + ex.Message);
                }
            }

            private string BuildCombinedPath(ExportRequest request)
            {
                string title = request.Drawing != null ? request.Drawing.Title : null;
                string baseName = PathSanitizer.SanitizeFileName(title, "Presentaciones");
                string reserved = new UniqueNameResolver(_folder).ReserveBaseName(baseName, ".pdf");
                return Path.Combine(_folder, reserved + ".pdf");
            }

            /// <summary>
            /// Fuerza el trazado/publicación en primer plano (BACKGROUNDPLOT = 0) y devuelve el valor
            /// anterior para poder restaurarlo, o null si ya estaba en primer plano o no se pudo leer.
            /// </summary>
            private static object TrySetForegroundPlot()
            {
                try
                {
                    object current = AcApp.GetSystemVariable("BACKGROUNDPLOT");
                    if (current is short s && s == 0) return null;

                    AcApp.SetSystemVariable("BACKGROUNDPLOT", (short)0);
                    return current;
                }
                catch (Exception)
                {
                    return null;
                }
            }

            /// <summary>
            /// Espera a que el archivo aparezca en disco, hasta <paramref name="timeout"/>. Con el
            /// trazado en primer plano el PDF ya existe al retornar EndPlot, pero este sondeo corto es
            /// un respaldo por si algún dispositivo lo vacía a disco con un instante de retraso.
            /// </summary>
            private static bool WaitForFile(string path, TimeSpan timeout)
            {
                DateTime deadline = DateTime.UtcNow + timeout;

                while (DateTime.UtcNow < deadline)
                {
                    if (File.Exists(path)) return true;
                    Thread.Sleep(100);
                }

                return File.Exists(path);
            }

            /// <summary>
            /// Points the plot settings at <paramref name="deviceName"/> and restores the layout's
            /// paper size when the device supports it. Without a canonical media name that belongs
            /// to the device, <see cref="PlotInfoValidator"/> fails with <c>eNoMatchingMedia</c>.
            /// </summary>
            private void EnsureDeviceAndMedia(
                PlotSettingsValidator validator,
                PlotSettings settings,
                string deviceName,
                string layoutName)
            {
                try
                {
                    // SIEMPRE se asigna el dispositivo, aunque el nombre copiado del layout ya
                    // coincida. CopyFrom trae el NOMBRE del dispositivo, no el vínculo con su
                    // configuración real: en un dibujo recién abierto el validator todavía no
                    // resolvió ese .pc3, y saltarse esta llamada hace que GetCanonicalMediaNameList
                    // opere sobre un estado sin inicializar y falle. El tamaño de papel se guarda
                    // antes y se restaura después, que es lo que esta llamada podría perder.
                    string desiredMedia = settings.CanonicalMediaName;

                    validator.SetPlotConfigurationName(settings, deviceName, null);
                    validator.RefreshLists(settings);

                    System.Collections.Specialized.StringCollection mediaCollection =
                        validator.GetCanonicalMediaNameList(settings);
                    if (mediaCollection.Count == 0) return;

                    var mediaList = new List<string>(mediaCollection.Count);
                    foreach (string m in mediaCollection)
                        mediaList.Add(m);

                    string media = PlotMediaResolver.Resolve(desiredMedia, mediaList);
                    if (media == null) return;

                    if (!string.IsNullOrWhiteSpace(desiredMedia) &&
                        !string.Equals(media, desiredMedia, StringComparison.OrdinalIgnoreCase))
                    {
                        _log.Warn(
                            $"La presentación '{layoutName}' pide el papel '{desiredMedia}', " +
                            $"pero el dispositivo \"{deviceName}\" no lo ofrece; se usará '{media}'.");
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
        }
    }
}
