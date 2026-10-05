using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.PlottingServices;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using GvrTools.Civil3D.Layouts;
using GvrTools.Civil3D.Model;
using GvrTools.Core.Batch;
using GvrTools.Core.Diagnostics;
using GvrTools.Core.IO;
using GvrTools.Core.Naming;

namespace GvrTools.Civil3D.Export.Plotting
{
    /// <summary>
    /// Plots layouts with AutoCAD's own plot engine (<see cref="PlotEngine"/> +
    /// <see cref="PlotSettings"/>/<see cref="PlotSettingsValidator"/>) through any device of the Plot
    /// dialog: PDF/DWF/PNG .pc3 files write one file per layout, Windows printers print (or write a
    /// .plt when "plot to file" is on).
    ///
    /// Two modes:
    ///   * one output per layout (default), and
    ///   * one multi-sheet file for the whole selection (<see cref="PlotExportSettings.CombineIntoSingleFile"/>,
    ///     PDF/DWF devices only), using AutoCAD's multi-sheet pipeline (a single BeginDocument, one
    ///     page per layout).
    ///
    /// AutoCAD refuses to plot a layout that is not the current one (<c>eLayoutNotCurrent</c>), so
    /// each layout is made current — with the document locked, since the run happens from a modeless
    /// window callback, not a command — right before it is plotted.
    /// </summary>
    public sealed class PlotExportEngine : IExportEngine
    {
        public ExportFormat Format => ExportFormat.Plot;

        public string StrategyDescription =>
            "Motor de trazado nativo de AutoCAD: cualquier impresora o .pc3 del cuadro Trazar, con las opciones de Civil 3D y los extras GVR.";

        public IExportSession BeginSession(ExportRequest request, IReadOnlyList<LayoutSnapshot> layouts)
        {
            PlotExportSettings settings = request.SettingsAs<PlotExportSettings>();

            if (PlotFactory.ProcessPlotState != ProcessPlotState.NotPlotting)
                throw new ExportSetupException("Ya hay un trazado en curso en AutoCAD. Espera a que termine e inténtalo de nuevo.");

            Document document = request.Document ?? AcApp.DocumentManager.MdiActiveDocument;
            if (document == null)
                throw new ExportSetupException("No hay un dibujo activo para trazar.");

            PlotDeviceDetails configured = null;
            if (settings.Source == PlotConfigSource.Manual)
            {
                configured = PlotDeviceRepository.GetDeviceDetails(settings.PlotDeviceName);
                if (configured == null)
                {
                    throw new ExportSetupException(
                        $"El dispositivo \"{settings.PlotDeviceName}\" no está disponible. Elige otro en la pestaña Trazado.");
                }

                if (configured.WritesFile(settings.PlotToFile) && !request.WritesFiles)
                    throw new ExportSetupException("Elige una carpeta de destino para los archivos trazados.");
            }

            return new Session(request, settings, configured, document, layouts ?? Array.Empty<LayoutSnapshot>());
        }

        /// <summary>Where one layout goes in a combined run, decided before the first page is plotted.</summary>
        private sealed class CombinedPagePlan
        {
            /// <summary>Why the layout cannot be plotted (it is skipped), or null.</summary>
            public string Error { get; set; }

            /// <summary>First page of a multi-sheet document (the run's first, or one with another paper).</summary>
            public bool StartsDocument { get; set; }

            /// <summary>Last page of its document: plotted with the "last page" flag, then the file is closed.</summary>
            public bool EndsDocument { get; set; }
        }

        private sealed class Session : IExportSession
        {
            private readonly Database _database;
            private readonly Document _document;
            private readonly ExportRequest _request;
            private readonly ExportFileNamer _namer;
            private readonly PlotExportSettings _settings;
            private readonly PlotInfoBuilder _builder;
            private readonly string _folder;
            private readonly ILog _log;
            private readonly object _previousBackgroundPlot;
            private readonly string _originalLayout;
            private readonly IReadOnlyList<LayoutSnapshot> _layouts;

            // Combined (multi-sheet) mode. Verified against AutoCAD 2027 (accoreconsole):
            //  * pages are buffered until one flagged "last" arrives — a document closed without it is
            //    written with ZERO pages, so the last valid page must be known before plotting;
            //  * only compatible pages share a document (PlotInfo.IsCompatibleDocument: same device and
            //    paper); mixing A1 and A4 fails with eInvalidPlotInfo;
            //  * every PlotInfo handed to the engine must stay alive until EndDocument.
            // So the run is planned up front (PlanCombinedPages): invalid layouts are skipped, and a
            // change of paper closes the file and continues in "<name>_2.pdf". The UI only offers
            // combining when every sheet gets the same paper, so the split is a safety net.
            private readonly bool _combine;
            private readonly string _combinedPath;
            private readonly List<PlotInfo> _combinedPageInfos = new List<PlotInfo>();
            private Dictionary<string, CombinedPagePlan> _plan;
            private PlotEngine _combinedEngine;
            private string _currentCombinedPath;
            private int _combinedFileCount;

            internal Session(ExportRequest request, PlotExportSettings settings, PlotDeviceDetails configured, Document document, IReadOnlyList<LayoutSnapshot> layouts)
            {
                _request = request;
                _database = request.Database;
                _document = document;
                _settings = settings;
                _folder = request.DestinationFolder;
                _log = request.Log;
                _layouts = layouts;
                _builder = new PlotInfoBuilder(settings, _log);
                _namer = new ExportFileNamer(
                    request.DestinationFolder ?? string.Empty,
                    request.NamingPattern,
                    configured?.DefaultFileExtension ?? ExportFormatInfo.Extension(ExportFormat.Plot),
                    request.Drawing.ToTokens());

                PlotOutputKind configuredOutput = configured?.ResolveOutput(settings.PlotToFile) ?? PlotOutputKind.Printer;
                _combine = settings.CombineIntoSingleFile &&
                           settings.Source == PlotConfigSource.Manual &&
                           PlotOutput.SupportsMultiSheet(configuredOutput);

                if (settings.CombineIntoSingleFile && !_combine)
                    _log.Info("Combinar en un solo archivo se ignora: el dispositivo o el origen de la configuración no lo permiten.");

                if (_combine)
                    _combinedPath = BuildCombinedPath(request, configured.DefaultFileExtension);

                try { _originalLayout = LayoutManager.Current.CurrentLayout; }
                catch (Exception) { _originalLayout = null; }

                // El motor de publicación de AutoCAD escribe el archivo en un proceso EN SEGUNDO PLANO
                // cuando BACKGROUNDPLOT está activo (valor por defecto en muchas instalaciones), así
                // que el archivo aparece DESPUÉS de que EndPlot retorna y File.Exists daría un falso
                // negativo aunque sí se cree. Forzamos primer plano durante el trazado.
                _previousBackgroundPlot = TrySetForegroundPlot();
            }

            public BatchItemResult Export(LayoutSnapshot layout)
            {
                return _combine ? ExportCombinedPage(layout) : ExportSingle(layout);
            }

            // ---------------------------------------------------------------- one output per layout

            private BatchItemResult ExportSingle(LayoutSnapshot layout)
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
                        PlotTarget target;
                        try
                        {
                            target = _builder.Build(plotInfo, liveLayout);
                        }
                        catch (PlotDeviceSetupException ex)
                        {
                            // Configuración inservible en ESTA presentación: cuesta la presentación,
                            // no el resto del lote.
                            _log.Warn($"No se pudo preparar el trazado de '{layout.Label}': {ex.Message}");
                            return BatchItemResult.Failure(layout.Label, ex.Message);
                        }

                        BatchItemResult result = target.WritesFile
                            ? PlotToFile(layout, plotInfo, target)
                            : PlotToDevice(layout, plotInfo, target);

                        tr.Commit();
                        return result;
                    }
                }
            }

            private BatchItemResult PlotToFile(LayoutSnapshot layout, PlotInfo plotInfo, PlotTarget target)
            {
                if (!_request.WritesFiles)
                {
                    return BatchItemResult.Failure(layout.Label,
                        $"\"{target.DeviceName}\" escribe archivos y no se eligió carpeta de destino.");
                }

                string baseName = _namer.ReserveBaseName(layout, target.Extension);
                string outputPath = Path.Combine(_folder, baseName + target.Extension);

                using (var engine = PlotFactory.CreatePublishEngine())
                {
                    engine.BeginPlot(null, null);
                    engine.BeginDocument(plotInfo, baseName, null, 1, true, outputPath);
                    PlotOnePage(engine, plotInfo, isLastPage: true);
                    engine.EndDocument(null);
                    engine.EndPlot(null);
                }

                if (!WaitForFile(outputPath, TimeSpan.FromSeconds(10)))
                {
                    _log.Warn($"AutoCAD reportó éxito pero no se encontró '{outputPath}'.");
                    return BatchItemResult.Failure(layout.Label, "AutoCAD no generó el archivo esperado.");
                }

                return BatchItemResult.Success(layout.Label, outputPath);
            }

            private BatchItemResult PlotToDevice(LayoutSnapshot layout, PlotInfo plotInfo, PlotTarget target)
            {
                int copies = Math.Max(1, Math.Min(_settings.Copies, 999));

                using (var engine = PlotFactory.CreatePublishEngine())
                {
                    engine.BeginPlot(null, null);
                    engine.BeginDocument(plotInfo, layout.Name, null, copies, false, null);
                    PlotOnePage(engine, plotInfo, isLastPage: true);
                    engine.EndDocument(null);
                    engine.EndPlot(null);
                }

                string detail = copies > 1
                    ? $"Enviado a \"{target.DeviceName}\" ({copies} copias)"
                    : $"Enviado a \"{target.DeviceName}\"";
                return BatchItemResult.SuccessWithoutFile(layout.Label, detail);
            }

            // ---------------------------------------------------------------- one multi-sheet file

            private BatchItemResult ExportCombinedPage(LayoutSnapshot layout)
            {
                if (_plan == null) _plan = PlanCombinedPages();

                // Una presentación que no estaba en la lista inicial no puede unirse sin romper el plan.
                if (!_plan.TryGetValue(layout.ObjectIdHandle, out CombinedPagePlan page))
                    return ExportSingle(layout);

                if (page.Error != null)
                {
                    _log.Warn($"No se pudo preparar el trazado de '{layout.Label}': {page.Error}");
                    return BatchItemResult.Failure(layout.Label, page.Error);
                }

                using (_document.LockDocument())
                using (var tr = _database.TransactionManager.StartTransaction())
                {
                    Layout liveLayout = LayoutRepository.Resolve(_database, tr, layout);
                    if (liveLayout == null)
                        return BatchItemResult.Failure(layout.Label, "La presentación ya no existe en el dibujo.");

                    SetCurrentLayout(liveLayout);

                    var pageInfo = new PlotInfo();
                    _combinedPageInfos.Add(pageInfo);

                    try
                    {
                        _builder.Build(pageInfo, liveLayout);
                    }
                    catch (PlotDeviceSetupException ex)
                    {
                        // Validó en el plan y ahora no: no debería pasar. Si era la última hoja del
                        // documento, se cierra igual para no dejarlo abierto.
                        _log.Warn($"No se pudo preparar el trazado de '{layout.Label}': {ex.Message}");
                        if (page.EndsDocument) CloseCombinedDocument();
                        return BatchItemResult.Failure(layout.Label, ex.Message);
                    }

                    if (page.StartsDocument || _combinedEngine == null)
                    {
                        CloseCombinedDocument();
                        StartCombinedDocument(pageInfo);
                    }

                    string path = _currentCombinedPath;
                    PlotOnePage(_combinedEngine, pageInfo, isLastPage: page.EndsDocument);

                    if (page.EndsDocument) CloseCombinedDocument();

                    tr.Commit();
                    return BatchItemResult.Success(layout.Label, path);
                }
            }

            /// <summary>
            /// Validates every layout of the run before the first page is plotted, so the engine knows
            /// which page closes each document and which layouts are skipped. Uses the same
            /// <see cref="PlotInfoBuilder"/> as the plot itself.
            /// </summary>
            private Dictionary<string, CombinedPagePlan> PlanCombinedPages()
            {
                var plan = new Dictionary<string, CombinedPagePlan>(StringComparer.Ordinal);
                var infos = new List<PlotInfo>();
                var quietBuilder = new PlotInfoBuilder(_settings, NullLog.Instance);
                PlotInfo documentInfo = null;
                CombinedPagePlan lastValid = null;
                int documents = 0;

                try
                {
                    using (_document.LockDocument())
                    using (var tr = _database.TransactionManager.StartTransaction())
                    {
                        foreach (LayoutSnapshot layout in _layouts)
                        {
                            var page = new CombinedPagePlan();
                            plan[layout.ObjectIdHandle] = page;

                            Layout liveLayout = LayoutRepository.Resolve(_database, tr, layout);
                            if (liveLayout == null)
                            {
                                page.Error = "La presentación ya no existe en el dibujo.";
                                continue;
                            }

                            SetCurrentLayout(liveLayout);

                            var info = new PlotInfo();
                            infos.Add(info);

                            try
                            {
                                quietBuilder.Build(info, liveLayout);
                            }
                            catch (PlotDeviceSetupException ex)
                            {
                                page.Error = ex.Message;
                                continue;
                            }

                            if (documentInfo == null || !documentInfo.IsCompatibleDocument(info))
                            {
                                if (lastValid != null) lastValid.EndsDocument = true;
                                page.StartsDocument = true;
                                documentInfo = info;
                                documents++;
                            }

                            lastValid = page;
                        }

                        tr.Commit();
                    }
                }
                finally
                {
                    foreach (PlotInfo info in infos)
                    {
                        try { info.Dispose(); } catch (Exception) { }
                    }
                }

                if (lastValid != null) lastValid.EndsDocument = true;

                if (documents > 1)
                {
                    _log.Warn($"Las presentaciones usan {documents} tamaños de papel consecutivos distintos y AutoCAD no los " +
                              $"mezcla en un mismo documento: se generarán {documents} archivos combinados.");
                }

                return plan;
            }

            private void StartCombinedDocument(PlotInfo firstPage)
            {
                _combinedFileCount++;
                _currentCombinedPath = _combinedFileCount == 1 ? _combinedPath : NextCombinedPath();

                WaitUntilPlotEngineIsFree();

                _combinedEngine = PlotFactory.CreatePublishEngine();
                _combinedEngine.BeginPlot(null, null);
                _combinedEngine.BeginDocument(
                    firstPage,
                    Path.GetFileNameWithoutExtension(_currentCombinedPath),
                    null,
                    1,
                    true,
                    _currentCombinedPath);
            }

            /// <summary>Closes the open multi-sheet document; AutoCAD writes the file here.</summary>
            private void CloseCombinedDocument()
            {
                if (_combinedEngine == null) return;

                try
                {
                    _combinedEngine.EndDocument(null);
                    _combinedEngine.EndPlot(null);
                }
                catch (Exception ex)
                {
                    _log.Warn("No se pudo finalizar el archivo combinado: " + ex.Message);
                }
                finally
                {
                    try { _combinedEngine.Dispose(); } catch (Exception) { }
                    _combinedEngine = null;
                }

                if (!WaitForFile(_currentCombinedPath, TimeSpan.FromSeconds(15)))
                    _log.Warn($"No se encontró el archivo combinado esperado '{_currentCombinedPath}'.");
            }

            /// <summary>"Planos_2.pdf", "Planos_3.pdf"... next to the first combined file.</summary>
            private string NextCombinedPath()
            {
                string extension = Path.GetExtension(_combinedPath);
                string baseName = Path.GetFileNameWithoutExtension(_combinedPath) + "_" + _combinedFileCount;
                string reserved = new UniqueNameResolver(_folder).ReserveBaseName(baseName, extension);
                return Path.Combine(_folder, reserved + extension);
            }

            /// <summary>
            /// AutoCAD reports itself as still plotting for a moment after EndPlot; starting the next
            /// document before it settles fails.
            /// </summary>
            private static void WaitUntilPlotEngineIsFree()
            {
                DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
                while (PlotFactory.ProcessPlotState != ProcessPlotState.NotPlotting && DateTime.UtcNow < deadline)
                    Thread.Sleep(150);
            }

            // ---------------------------------------------------------------- shared plumbing

            /// <summary>Makes <paramref name="layout"/> the current layout so AutoCAD will plot it.</summary>
            private static void SetCurrentLayout(Layout layout)
            {
                LayoutManager lm = LayoutManager.Current;
                if (!string.Equals(lm.CurrentLayout, layout.LayoutName, StringComparison.Ordinal))
                    lm.CurrentLayout = layout.LayoutName;
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
                // Cierra el documento de trazado del archivo combinado (el archivo se escribe aquí).
                CloseCombinedDocument();

                foreach (PlotInfo info in _combinedPageInfos)
                {
                    try { info.Dispose(); } catch (Exception) { }
                }

                _combinedPageInfos.Clear();

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

            private string BuildCombinedPath(ExportRequest request, string extension)
            {
                string title = request.Drawing != null ? request.Drawing.Title : null;
                string baseName = PathSanitizer.SanitizeFileName(title, "Presentaciones");
                string reserved = new UniqueNameResolver(_folder).ReserveBaseName(baseName, extension);
                return Path.Combine(_folder, reserved + PlotOutput.NormalizeExtension(extension));
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
            /// trazado en primer plano el archivo ya existe al retornar EndPlot, pero este sondeo corto
            /// es un respaldo por si algún dispositivo lo vacía a disco con un instante de retraso.
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
        }
    }
}
