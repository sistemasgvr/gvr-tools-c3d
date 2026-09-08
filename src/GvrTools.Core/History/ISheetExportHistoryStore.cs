using System;
using System.Collections.Generic;

namespace GvrTools.Core.History
{
    /// <summary>
    /// Per-project memory of when each sheet was last exported successfully, so the UI can show
    /// "never exported" / "exported 2 days ago" without depending on Revit for a modification
    /// timestamp (Revit has no reliable per-element last-changed time outside worksharing).
    /// </summary>
    public interface ISheetExportHistoryStore
    {
        /// <summary>
        /// Returns the stored history for <paramref name="projectKey"/> (sheet unique id -> last
        /// successful export, UTC), or an empty dictionary when nothing is stored yet or the
        /// stored data is unreadable. Never throws.
        /// </summary>
        IReadOnlyDictionary<string, DateTime> Load(string projectKey);

        /// <summary>Persists <paramref name="history"/> under <paramref name="projectKey"/>. Never throws.</summary>
        void Save(string projectKey, IReadOnlyDictionary<string, DateTime> history);
    }
}
