using System;

namespace GvrTools.Civil3D.Infrastructure
{
    /// <summary>One step-driven job (a batch export run, one layout per step).</summary>
    public interface ICivilStepJob
    {
        string Name { get; }

        int StepCount { get; }

        void Begin();

        void ExecuteStep(int stepIndex);

        void End(bool cancelled, Exception failure);
    }
}
