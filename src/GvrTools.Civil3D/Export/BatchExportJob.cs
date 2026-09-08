using System;
using System.Collections.Generic;
using System.Diagnostics;
using GvrTools.Civil3D.Infrastructure;
using GvrTools.Civil3D.Model;
using GvrTools.Core.Batch;
using GvrTools.Core.IO;

namespace GvrTools.Civil3D.Export
{
    /// <summary>
    /// Drives one export run as a step job: one layout per step, so the scheduler can hand control
    /// back to the message loop in between and the window stays responsive and cancellable.
    /// Equivalent to Revit's <c>BatchExportJob</c>.
    /// </summary>
    public sealed class BatchExportJob : ICivilStepJob
    {
        private readonly IExportEngine _engine;
        private readonly ExportRequest _request;
        private readonly IReadOnlyList<LayoutSnapshot> _layouts;
        private readonly Action<BatchProgress> _onProgress;
        private readonly Action<BatchItemResult> _onItemCompleted;
        private readonly Action<BatchResult> _onFinished;
        private readonly List<BatchItemResult> _results;
        private readonly Stopwatch _stopwatch = new Stopwatch();

        private IExportSession _session;

        public BatchExportJob(
            IExportEngine engine,
            ExportRequest request,
            IReadOnlyList<LayoutSnapshot> layouts,
            Action<BatchProgress> onProgress = null,
            Action<BatchItemResult> onItemCompleted = null,
            Action<BatchResult> onFinished = null)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _request = request ?? throw new ArgumentNullException(nameof(request));
            _layouts = layouts ?? throw new ArgumentNullException(nameof(layouts));
            _onProgress = onProgress;
            _onItemCompleted = onItemCompleted;
            _onFinished = onFinished;
            _results = new List<BatchItemResult>(layouts.Count);
        }

        public string Name => $"Exportación {ExportFormatInfo.Label(_engine.Format)}";

        public int StepCount => _layouts.Count;

        public void Begin()
        {
            _stopwatch.Restart();
            CreateDestinationFolder();

            _request.Log.Info($"{Name}: {_layouts.Count} presentación(es) hacia '{_request.DestinationFolder}' " +
                              $"({_engine.StrategyDescription}).");

            _session = _engine.BeginSession(_request, _layouts.Count);
        }

        public void ExecuteStep(int stepIndex)
        {
            LayoutSnapshot layout = _layouts[stepIndex];
            _onProgress?.Invoke(new BatchProgress(stepIndex, _layouts.Count, layout.Label));

            BatchItemResult result = ExportOne(layout);
            _results.Add(result);
            _onItemCompleted?.Invoke(result);

            _onProgress?.Invoke(new BatchProgress(stepIndex + 1, _layouts.Count, layout.Label));
        }

        public void End(bool cancelled, Exception failure)
        {
            try
            {
                _session?.Dispose();
            }
            catch (Exception ex)
            {
                _request.Log.Warn("El cierre de la sesión de exportación falló: " + ex.Message);
            }
            finally
            {
                _session = null;
                _stopwatch.Stop();
            }

            string setupError = failure is ExportSetupException setup
                ? setup.Message
                : failure != null
                    ? "Error inesperado durante la exportación: " + failure.Message
                    : null;

            var result = new BatchResult(_results, cancelled, _request.DestinationFolder, _stopwatch.Elapsed, setupError);

            _request.Log.Info($"{Name} finalizada: {result.SucceededCount} correcta(s), {result.FailedCount} con error, " +
                              $"cancelada={cancelled}, {result.Elapsed.TotalSeconds:0.0} s.");

            _onFinished?.Invoke(result);
        }

        /// <summary>A per-layout failure never aborts the batch.</summary>
        private BatchItemResult ExportOne(LayoutSnapshot layout)
        {
            try
            {
                return _session.Export(layout);
            }
            catch (Exception ex)
            {
                _request.Log.Error($"Fallo al exportar {layout.Label}.", ex);
                return BatchItemResult.Failure(layout.Label, ex.Message);
            }
        }

        private void CreateDestinationFolder()
        {
            if (ExportPathHelper.TryEnsureWritable(_request.DestinationFolder, out string error))
                return;

            throw new ExportSetupException(error);
        }
    }
}
