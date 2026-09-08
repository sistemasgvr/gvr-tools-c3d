using System;
using System.IO;
using System.Threading;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.PlottingServices;
using PlotType = Autodesk.AutoCAD.DatabaseServices.PlotType;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using GvrTools.Civil3D.Layouts;
using GvrTools.Civil3D.Model;
using GvrTools.Core.Batch;
using GvrTools.Core.Diagnostics;
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
            "Motor de trazado nativo de AutoCAD (DWG To PDF.pc3): sin ventanas y sin bloquear el equipo.";

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
                        BuildPlotInfo(plotInfo, liveLayout);

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
                        BuildPlotInfo(plotInfo, liveLayout);

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

                // Una exportación a PDF SIEMPRE debe trazar con un dispositivo ePlot que produzca PDF
                // real (DWG To PDF.pc3). NO se puede confiar en el dispositivo de la configuración de
                // página de la presentación: si el cajetín apunta a una impresora del sistema (p. ej.
                // un driver PScript), el resultado es un archivo .pdf que en realidad es PostScript y
                // no abre. La opción "usar configuración de página" conserva el TAMAÑO del layout
                // (EnsureDeviceAndMedia mantiene su medio si el dispositivo PDF lo soporta), pero el
                // dispositivo de salida se fuerza aquí siempre.
                EnsureDeviceAndMedia(validator, plotSettings, _settings.PlotDeviceName);

                if (_settings.FitToPaper)
                    validator.SetPlotType(plotSettings, PlotType.Extents);

                validator.SetUseStandardScale(plotSettings, true);
                StdScaleType scaleType = _settings.FitToPaper ? StdScaleType.ScaleToFit : plotSettings.StdScaleType;
                validator.SetStdScaleType(plotSettings, scaleType);
                validator.SetPlotCentered(plotSettings, true);

                if (!string.IsNullOrWhiteSpace(_settings.PlotStyleTableOverride))
                    validator.SetCurrentStyleSheet(plotSettings, _settings.PlotStyleTableOverride);

                plotInfo.OverrideSettings = plotSettings;

                var validity = new PlotInfoValidator { MediaMatchingPolicy = MatchingPolicy.MatchEnabled };
                validity.Validate(plotInfo);
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
            /// Points the plot settings at <paramref name="deviceName"/> and guarantees the paper
            /// size is one the device actually supports. Without a canonical media name that belongs
            /// to the device, <see cref="PlotInfoValidator"/> fails with <c>eNoMatchingMedia</c>. The
            /// layout's original size is preserved when the device supports it; otherwise a common
            /// default is chosen so the export still produces a PDF.
            /// </summary>
            private static void EnsureDeviceAndMedia(PlotSettingsValidator validator, PlotSettings settings, string deviceName)
            {
                try
                {
                    validator.SetPlotConfigurationName(settings, deviceName, null);
                    validator.RefreshLists(settings);

                    System.Collections.Specialized.StringCollection mediaList = validator.GetCanonicalMediaNameList(settings);
                    if (mediaList.Count == 0) return;

                    string current = settings.CanonicalMediaName;
                    if (string.IsNullOrWhiteSpace(current) || !mediaList.Contains(current))
                        validator.SetCanonicalMediaName(settings, PickDefaultMedia(mediaList));
                }
                catch (Exception ex)
                {
                    throw new ExportSetupException(
                        $"No se pudo configurar el dispositivo de trazado \"{deviceName}\". " +
                        "Verifica que esté instalado (Administrador de trazadores de AutoCAD).", ex);
                }
            }

            private static string PickDefaultMedia(System.Collections.Specialized.StringCollection mediaList)
            {
                // Preferir un tamaño común y ampliamente disponible en DWG To PDF.pc3, en orden.
                string[] preferred = { "ISO_A4_", "ISO_full_bleed_A4_", "ANSI_A_", "A4", "Letter" };

                foreach (string want in preferred)
                    foreach (string media in mediaList)
                        if (media != null && media.IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0)
                            return media;

                return mediaList[0];
            }
        }
    }
}
