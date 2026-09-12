using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.PlottingServices;
using GvrTools.Civil3D.Infrastructure;
using GvrTools.Civil3D.Layouts;
using GvrTools.Civil3D.Model;
using GvrTools.Core.Batch;
using GvrTools.Core.Diagnostics;
using GvrTools.Core.IO;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace GvrTools.Civil3D.Export
{
    /// <summary>
    /// Exports every layout of every DWG in a folder to PDF without the user ever opening them by
    /// hand: one file per step, each step opens its DWG in this same AutoCAD/Civil 3D session,
    /// plots its layouts, and closes it again before moving to the next.
    ///
    /// AutoCAD has no "read the database only" entry point that also initialises Civil 3D's own
    /// object model (alignments, surfaces, pipe networks...) the way a real <see cref="Document"/>
    /// does, so each file is opened as a genuine (but never shown-to/touched-by-the-user) document
    /// via <see cref="DocumentCollection.Open"/> rather than a bare <c>Database.ReadDwgFile</c>.
    /// Opening a document does make it the active one, so the previously active document is
    /// restored once the whole run ends (not after each file, to avoid needless flicker).
    /// </summary>
    public sealed class FolderBatchExportJob : ICivilStepJob
    {
        private readonly IReadOnlyList<string> _dwgPaths;
        private readonly Func<Document, DrawingSnapshot, ExportRequest> _requestFactory;
        private readonly IExportEngine _engine;
        private readonly Action<BatchProgress> _onProgress;
        private readonly Action<BatchItemResult> _onItemCompleted;
        private readonly Action<BatchResult> _onFinished;
        private readonly ILog _log;
        private readonly List<BatchItemResult> _results;
        private readonly System.Diagnostics.Stopwatch _stopwatch = new System.Diagnostics.Stopwatch();

        private Document _originalActiveDocument;
        private string _lastDestinationFolder;

        /// <summary>
        /// True once any drawing has produced its PDFs. It is what separates "this environment
        /// cannot export at all" from "this particular drawing is broken": the first is worth
        /// aborting the batch for, the second is not.
        /// </summary>
        private bool _anyDrawingExported;

        public FolderBatchExportJob(
            IReadOnlyList<string> dwgPaths,
            IExportEngine engine,
            Func<Document, DrawingSnapshot, ExportRequest> requestFactory,
            ILog log,
            Action<BatchProgress> onProgress = null,
            Action<BatchItemResult> onItemCompleted = null,
            Action<BatchResult> onFinished = null)
        {
            _dwgPaths = dwgPaths ?? throw new ArgumentNullException(nameof(dwgPaths));
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _requestFactory = requestFactory ?? throw new ArgumentNullException(nameof(requestFactory));
            _log = log ?? NullLog.Instance;
            _onProgress = onProgress;
            _onItemCompleted = onItemCompleted;
            _onFinished = onFinished;
            _results = new List<BatchItemResult>(dwgPaths.Count);
        }

        public string Name => "Exportación masiva de dibujos a PDF";

        public int StepCount => _dwgPaths.Count;

        public void Begin()
        {
            _stopwatch.Restart();
            _originalActiveDocument = AcApp.DocumentManager.MdiActiveDocument;
            _log.Info($"{Name}: {_dwgPaths.Count} dibujo(s).");
        }

        public void ExecuteStep(int stepIndex)
        {
            string path = _dwgPaths[stepIndex];
            string label = Path.GetFileName(path);

            _onProgress?.Invoke(new BatchProgress(stepIndex, _dwgPaths.Count, label));

            BatchItemResult result = ExportOneDrawing(path, label);
            _results.Add(result);
            _onItemCompleted?.Invoke(result);

            _onProgress?.Invoke(new BatchProgress(stepIndex + 1, _dwgPaths.Count, label));
        }

        public void End(bool cancelled, Exception failure)
        {
            RestoreOriginalActiveDocument();
            _stopwatch.Stop();

            string setupError = failure is ExportSetupException setup
                ? setup.Message
                : failure != null
                    ? "Error inesperado durante la exportación: " + failure.Message
                    : null;

            var result = new BatchResult(_results, cancelled, _lastDestinationFolder, _stopwatch.Elapsed, setupError);

            _log.Info($"{Name} finalizada: {result.SucceededCount} correcta(s), {result.FailedCount} con error, " +
                      $"cancelada={cancelled}, {result.Elapsed.TotalSeconds:0.0} s.");

            _onFinished?.Invoke(result);
        }

        /// <summary>A failure on one drawing (corrupt file, missing xrefs...) never aborts the batch.</summary>
        private BatchItemResult ExportOneDrawing(string path, string label)
        {
            if (!File.Exists(path))
                return BatchItemResult.Failure(label, "El archivo ya no existe en disco.");

            // Un dibujo que el usuario ya tiene abierto (posiblemente con cambios sin guardar) nunca
            // se toca: reabrirlo fallaría de todos modos (AutoCAD no permite dos instancias del mismo
            // archivo en una sesión), pero además cerrarlo al terminar descartaría ese trabajo.
            Document alreadyOpen = FindOpenDocument(path);
            if (alreadyOpen != null)
                return BatchItemResult.Skipped(label,
                    "El dibujo debe estar cerrado. Ciérralo en Civil 3D (guarda si hace falta) e inténtalo de nuevo.");

            Document document = null;
            try
            {
                document = OpenInBackground(path);

                DrawingSnapshot drawing = DrawingSnapshot.Read(path);
                ExportRequest request = _requestFactory(document, drawing);
                _lastDestinationFolder = request.DestinationFolder;

                IReadOnlyList<LayoutSnapshot> layouts = LayoutRepository.GetLayouts(document.Database);
                if (layouts.Count == 0)
                    return BatchItemResult.Skipped(label, "El dibujo no tiene presentaciones para exportar.");

                WaitUntilPlotEngineIsFree();

                using (IExportSession session = _engine.BeginSession(request, layouts.Count))
                {
                    int failedCount = 0;
                    foreach (LayoutSnapshot layout in layouts)
                    {
                        BatchItemResult layoutResult = session.Export(layout);
                        if (!layoutResult.Succeeded) failedCount++;
                    }

                    if (failedCount < layouts.Count)
                        _anyDrawingExported = true;

                    return failedCount == 0
                        ? BatchItemResult.Success(label, request.DestinationFolder)
                        : BatchItemResult.Failure(label, $"{failedCount} de {layouts.Count} presentación(es) fallaron.");
                }
            }
            catch (ExportSetupException ex) when (_anyDrawingExported)
            {
                // Una vez que un dibujo se exportó con éxito, el entorno (dispositivo de trazado,
                // carpeta de destino) está probado: lo que falle después es problema de ESE dibujo,
                // no del lote, así que cuesta un archivo en vez de cancelarlo todo.
                _log.Error($"Fallo de configuración al exportar '{label}'.", ex);
                return BatchItemResult.Failure(label, ex.Message);
            }
            catch (ExportSetupException)
            {
                // Falla el primer dibujo: el entorno nunca llegó a funcionar (no hay dispositivo PDF,
                // la carpeta no es escribible...), así que seguir con los demás solo repetiría el
                // mismo error N veces.
                throw;
            }
            catch (Exception ex)
            {
                _log.Error($"Fallo al exportar '{label}'.", ex);
                return BatchItemResult.Failure(label, ex.Message);
            }
            finally
            {
                CloseWithoutSaving(document, label);
            }
        }

        /// <summary>
        /// Opens <paramref name="path"/> in this session. AutoCAD's managed API has no way to open a
        /// document without making it the active one (there is no hidden/background open), so each
        /// file will briefly flash as the active MDI tab while its layouts are exported; the
        /// document that was active before the run started is restored once every file is done.
        /// </summary>
        private static Document OpenInBackground(string path) => AcApp.DocumentManager.Open(path, false);

        /// <summary>
        /// Gives AutoCAD a moment to settle back to <see cref="ProcessPlotState.NotPlotting"/> between
        /// files. Each drawing opens and closes its own plot engine in quick succession, and the
        /// engine reports itself as still plotting for a short while after the previous file's
        /// EndPlot returns; without this wait the next <c>BeginSession</c> would raise a setup error
        /// and abort the whole batch.
        /// </summary>
        private static void WaitUntilPlotEngineIsFree()
        {
            DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);

            while (PlotFactory.ProcessPlotState != ProcessPlotState.NotPlotting && DateTime.UtcNow < deadline)
                System.Threading.Thread.Sleep(150);
        }

        /// <summary>Finds a document already open in this session with the same file path, if any.</summary>
        private static Document FindOpenDocument(string path)
        {
            foreach (Document doc in AcApp.DocumentManager)
            {
                try
                {
                    if (string.Equals(doc.Name, path, StringComparison.OrdinalIgnoreCase))
                        return doc;
                }
                catch (Exception)
                {
                    // Un documento en un estado extraño (p. ej. cerrándose en otro hilo) no cuenta
                    // como "abierto" para este chequeo; seguir con el resto de la lista.
                }
            }

            return null;
        }

        private void CloseWithoutSaving(Document document, string label)
        {
            if (document == null) return;

            try
            {
                document.CloseAndDiscard();
            }
            catch (Exception ex)
            {
                _log.Warn($"No se pudo cerrar '{label}' tras exportarlo: {ex.Message}");
            }
        }

        private void RestoreOriginalActiveDocument()
        {
            if (_originalActiveDocument == null) return;

            try
            {
                bool stillOpen = false;
                foreach (Document doc in AcApp.DocumentManager)
                {
                    if (ReferenceEquals(doc, _originalActiveDocument)) { stillOpen = true; break; }
                }

                if (stillOpen)
                    AcApp.DocumentManager.MdiActiveDocument = _originalActiveDocument;
            }
            catch (Exception ex)
            {
                _log.Warn("No se pudo restaurar el dibujo originalmente activo: " + ex.Message);
            }
            finally
            {
                _originalActiveDocument = null;
            }
        }
    }
}
