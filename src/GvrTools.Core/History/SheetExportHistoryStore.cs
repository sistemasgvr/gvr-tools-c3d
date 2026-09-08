using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using GvrTools.Core.Naming;

namespace GvrTools.Core.History
{
    /// <summary>
    /// Stores each project's export history as a flat <c>sheetUniqueId=utcTicks</c> text file under
    /// <c>%APPDATA%\GVR\GvrTools\export-history\</c>, one file per project.
    ///
    /// Same reasoning as <see cref="Settings.FlatFileSettingsStore"/>: plain text, not JSON, to
    /// avoid shipping another serialiser into Revit's process. A dictionary (not flat scalars) is
    /// still simple enough to hand-roll as one <c>key=value</c> line per sheet.
    /// </summary>
    public sealed class SheetExportHistoryStore : ISheetExportHistoryStore
    {
        private readonly string _directory;

        public SheetExportHistoryStore(string directory = null)
        {
            _directory = directory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "GVR", "GvrTools", "export-history");
        }

        public IReadOnlyDictionary<string, DateTime> Load(string projectKey)
        {
            var history = new Dictionary<string, DateTime>(StringComparer.Ordinal);

            try
            {
                string path = PathFor(projectKey);
                if (!File.Exists(path)) return history;

                foreach (string line in File.ReadAllLines(path))
                {
                    int separator = line.IndexOf('=');
                    if (separator <= 0) continue;

                    string sheetUniqueId = line.Substring(0, separator);
                    string rawTicks = line.Substring(separator + 1);
                    if (long.TryParse(rawTicks, NumberStyles.Integer, CultureInfo.InvariantCulture, out long ticks))
                        history[sheetUniqueId] = new DateTime(ticks, DateTimeKind.Utc);
                }
            }
            catch
            {
                // Unreadable or corrupt history simply means "nothing remembered yet".
                return new Dictionary<string, DateTime>(StringComparer.Ordinal);
            }

            return history;
        }

        public void Save(string projectKey, IReadOnlyDictionary<string, DateTime> history)
        {
            if (history == null) return;

            try
            {
                Directory.CreateDirectory(_directory);

                var sb = new StringBuilder();
                foreach (KeyValuePair<string, DateTime> entry in history)
                {
                    if (string.IsNullOrEmpty(entry.Key)) continue;
                    sb.Append(entry.Key).Append('=')
                      .Append(entry.Value.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture))
                      .AppendLine();
                }

                File.WriteAllText(PathFor(projectKey), sb.ToString(), Encoding.UTF8);
            }
            catch
            {
                // Remembering history is a convenience, never a reason to fail an export run.
            }
        }

        private string PathFor(string projectKey) =>
            Path.Combine(_directory, PathSanitizer.SanitizeFileName(projectKey, "project") + ".history");
    }
}
