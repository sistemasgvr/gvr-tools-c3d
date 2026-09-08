using System;

namespace GvrTools.Core.Diagnostics
{
    /// <summary>No-op log, so no call site ever needs a null check.</summary>
    public sealed class NullLog : ILog
    {
        public static readonly ILog Instance = new NullLog();

        private NullLog() { }

        public void Info(string message) { }

        public void Warn(string message) { }

        public void Error(string message, Exception exception = null) { }
    }
}
