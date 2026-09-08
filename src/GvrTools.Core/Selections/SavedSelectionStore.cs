using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using GvrTools.Core.Naming;

namespace GvrTools.Core.Selections
{
    /// <summary>
    /// Stores each project's saved selection filters as a flat text file under
    /// <c>%APPDATA%\GVR\GvrTools\saved-selections\</c>, one file per project.
    ///
    /// Same reasoning as <see cref="Settings.FlatFileSettingsStore"/>/<see cref="History.SheetExportHistoryStore"/>:
    /// plain text, not JSON, to avoid shipping another serialiser into Revit's process. Format is a
    /// "#kind|name" header line followed by one UniqueId per line, repeated per filter:
    /// <code>
    /// #Sheet|Eléctricas
    /// 36c1f4b2-0000-0000-0000-00000000000c
    /// 36c1f4b2-0000-0000-0000-00000000010c
    /// #Sheet|Estructuras
    /// ...
    /// </code>
    /// </summary>
    public sealed class SavedSelectionStore : ISavedSelectionStore
    {
        private readonly string _directory;

        public SavedSelectionStore(string directory = null)
        {
            _directory = directory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "GVR", "GvrTools", "saved-selections");
        }

        public IReadOnlyList<SavedSelection> Load(string projectKey)
        {
            try
            {
                string path = PathFor(projectKey);
                if (!File.Exists(path)) return Array.Empty<SavedSelection>();

                var result = new List<SavedSelection>();
                string currentName = null;
                string currentKind = string.Empty;
                var currentIds = new List<string>();

                void Flush()
                {
                    if (currentName != null)
                        result.Add(new SavedSelection(currentName, currentKind, currentIds));
                }

                foreach (string line in File.ReadAllLines(path))
                {
                    if (line.Length == 0) continue;

                    if (line[0] == '#')
                    {
                        Flush();

                        string header = line.Substring(1);
                        int separator = header.IndexOf('|');
                        if (separator >= 0)
                        {
                            currentKind = header.Substring(0, separator);
                            currentName = header.Substring(separator + 1);
                        }
                        else
                        {
                            // Older file without a kind prefix -- treat the whole header as the name.
                            currentKind = string.Empty;
                            currentName = header;
                        }

                        currentIds = new List<string>();
                    }
                    else if (currentName != null)
                    {
                        currentIds.Add(line);
                    }
                }

                Flush();
                return result;
            }
            catch
            {
                // Unreadable or corrupt file simply means "no filters saved yet".
                return Array.Empty<SavedSelection>();
            }
        }

        public void Save(string projectKey, IReadOnlyList<SavedSelection> selections)
        {
            if (selections == null) return;

            try
            {
                Directory.CreateDirectory(_directory);

                var sb = new StringBuilder();
                foreach (SavedSelection selection in selections)
                {
                    string safeName = SanitizeName(selection.Name);
                    if (safeName.Length == 0) continue;

                    string safeKind = (selection.Kind ?? string.Empty).Replace("|", string.Empty)
                        .Replace("\r", string.Empty).Replace("\n", string.Empty);

                    sb.Append('#').Append(safeKind).Append('|').AppendLine(safeName);
                    foreach (string uniqueId in selection.UniqueIds.Where(id => !string.IsNullOrWhiteSpace(id)))
                        sb.AppendLine(uniqueId);
                }

                File.WriteAllText(PathFor(projectKey), sb.ToString(), Encoding.UTF8);
            }
            catch
            {
                // Remembering filters is a convenience, never a reason to fail an export run.
            }
        }

        /// <summary>
        /// Strips the characters that would corrupt the "#kind|name" line format ('#', '|',
        /// newlines). Exposed so a caller (the ViewModel) can validate/dedupe against the EXACT
        /// string that will end up on disk instead of against the raw name the user typed --
        /// otherwise two names that only differ in stripped characters (e.g. "Eléctricas" and
        /// "Eléctricas#") pass an in-memory dedup check but collide into the same saved line, and a
        /// name that sanitizes to nothing (e.g. "###") gets silently dropped with no feedback.
        /// </summary>
        public static string SanitizeName(string name) =>
            (name ?? string.Empty).Replace("#", string.Empty).Replace("|", string.Empty)
                .Replace("\r", string.Empty).Replace("\n", string.Empty).Trim();

        private string PathFor(string projectKey) =>
            Path.Combine(_directory, PathSanitizer.SanitizeFileName(projectKey, "project") + ".selections");
    }
}
