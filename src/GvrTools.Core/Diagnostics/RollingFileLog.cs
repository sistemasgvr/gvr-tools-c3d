using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace GvrTools.Core.Diagnostics
{
    /// <summary>
    /// Appends one line per entry to a per-day file under
    /// <c>%LOCALAPPDATA%\GVR\GvrTools\logs\</c>, keeping only the most recent files.
    ///
    /// Logging must never be the reason an add-in fails, so every operation swallows its own
    /// errors: the worst case is a missing log line, not a broken export.
    /// </summary>
    public sealed class RollingFileLog : ILog
    {
        private const int MaxFilesKept = 10;

        private readonly object _gate = new object();
        private readonly string _directory;
        private readonly string _scope;

        public RollingFileLog(string scope, string directory = null)
        {
            _scope = string.IsNullOrWhiteSpace(scope) ? "GvrTools" : scope;
            _directory = directory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GVR", "GvrTools", "logs");
        }

        /// <summary>Full path of today's log file, so the UI can offer to open it.</summary>
        public string CurrentFilePath =>
            Path.Combine(_directory, "gvrtools-" + DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log");

        public void Info(string message) => Write("INFO ", message, null);

        public void Warn(string message) => Write("WARN ", message, null);

        public void Error(string message, Exception exception = null) => Write("ERROR", message, exception);

        private void Write(string level, string message, Exception exception)
        {
            try
            {
                lock (_gate)
                {
                    Directory.CreateDirectory(_directory);

                    var line = new StringBuilder()
                        .Append(DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture))
                        .Append(' ').Append(level)
                        .Append(" [").Append(_scope).Append("] ")
                        .Append(message);

                    if (exception != null)
                        line.AppendLine().Append(exception);

                    File.AppendAllText(CurrentFilePath, line.AppendLine().ToString(), Encoding.UTF8);
                    TrimOldFiles();
                }
            }
            catch
            {
                // Intentionally ignored — see class remarks.
            }
        }

        private void TrimOldFiles()
        {
            try
            {
                string[] stale = Directory.GetFiles(_directory, "gvrtools-*.log")
                    .OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase)
                    .Skip(MaxFilesKept)
                    .ToArray();

                foreach (string file in stale)
                    File.Delete(file);
            }
            catch
            {
                // Intentionally ignored — see class remarks.
            }
        }
    }
}
