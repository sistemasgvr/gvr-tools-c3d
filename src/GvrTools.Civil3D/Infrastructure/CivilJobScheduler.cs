using System;
using System.Windows.Threading;
using GvrTools.Core.Diagnostics;

namespace GvrTools.Civil3D.Infrastructure
{
    /// <summary>
    /// Runs an <see cref="ICivilStepJob"/> one step at a time, giving the message loop a turn in
    /// between so the tool window (and AutoCAD) stay responsive and the run stays cancellable.
    ///
    /// Revit's equivalent (<c>RevitJobScheduler</c>) has to go through an <c>ExternalEvent</c>
    /// because the Revit API can only be touched from a callback Revit itself invokes. AutoCAD's
    /// managed API has no such restriction — plotting and database access work fine from a plain
    /// command context or a background-priority dispatcher callback — so this scheduler is a
    /// straightforward "post the next step, then yield" loop instead of an event-pump wrapper.
    /// </summary>
    public sealed class CivilJobScheduler : IDisposable
    {
        private readonly Dispatcher _dispatcher;
        private readonly ILog _log;
        private ICivilStepJob _job;
        private int _nextStep;
        private bool _cancelRequested;
        private bool _disposed;

        public CivilJobScheduler(ILog log = null)
        {
            _log = log ?? NullLog.Instance;
            _dispatcher = Dispatcher.CurrentDispatcher;
        }

        public bool IsRunning => _job != null;

        /// <summary>Queues <paramref name="job"/>. Returns immediately; the job runs step by step.</summary>
        public void Start(ICivilStepJob job)
        {
            if (job == null) throw new ArgumentNullException(nameof(job));
            if (_disposed) throw new ObjectDisposedException(nameof(CivilJobScheduler));
            if (IsRunning) throw new InvalidOperationException("Ya hay una operación en curso en este programador de tareas.");

            _log.Info($"Iniciando trabajo '{job.Name}' ({job.StepCount} paso(s)).");
            _job = job;
            _nextStep = 0;
            _cancelRequested = false;

            try
            {
                job.Begin();
            }
            catch (Exception ex)
            {
                _log.Error($"El trabajo '{job.Name}' no pudo iniciar.", ex);
                Finish(job, false, ex);
                return;
            }

            ScheduleNextStep();
        }

        public void RequestCancel() => _cancelRequested = true;

        public void Dispose()
        {
            _disposed = true;
        }

        private void ScheduleNextStep()
        {
            if (_dispatcher != null && !_dispatcher.HasShutdownStarted)
                _dispatcher.BeginInvoke(new Action(RunOneStep), DispatcherPriority.Background);
            else
                RunOneStep();
        }

        private void RunOneStep()
        {
            ICivilStepJob job = _job;
            if (job == null || _disposed) return;

            try
            {
                if (!_cancelRequested && _nextStep < job.StepCount)
                {
                    job.ExecuteStep(_nextStep);
                    _nextStep++;
                }

                if (_cancelRequested || _nextStep >= job.StepCount)
                    Finish(job, _cancelRequested, null);
                else
                    ScheduleNextStep();
            }
            catch (Exception ex)
            {
                _log.Error($"El trabajo '{job.Name}' se interrumpió por un error.", ex);
                Finish(job, _cancelRequested, ex);
            }
        }

        private void Finish(ICivilStepJob job, bool cancelled, Exception failure)
        {
            _job = null;

            try
            {
                job.End(cancelled, failure);
            }
            catch (Exception ex)
            {
                _log.Error($"El cierre del trabajo '{job.Name}' falló.", ex);
            }
        }
    }
}
