using System;

namespace GvrTools.Core.Diagnostics
{
    /// <summary>
    /// Minimal logging contract. Kept tiny on purpose: a Revit add-in must not drag a logging
    /// framework into the host process, and every tool only ever needs these three levels.
    /// </summary>
    public interface ILog
    {
        void Info(string message);

        void Warn(string message);

        void Error(string message, Exception exception = null);
    }
}
