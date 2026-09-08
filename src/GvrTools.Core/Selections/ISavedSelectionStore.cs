using System.Collections.Generic;

namespace GvrTools.Core.Selections
{
    /// <summary>
    /// A named subset of sheets/views a user marked once and wants to reapply later without
    /// re-checking every row by hand ("filtro por especialidad": Eléctricas, Estructuras...).
    /// </summary>
    public sealed class SavedSelection
    {
        public SavedSelection(string name, string kind, IReadOnlyList<string> uniqueIds)
        {
            Name = name;
            Kind = kind ?? string.Empty;
            UniqueIds = uniqueIds;
        }

        public string Name { get; }

        /// <summary>
        /// Free-form tag the caller uses to keep filters scoped to what they were built from (e.g.
        /// "Sheet" vs "View" in GvrTools.Tools.BatchExport) -- the store itself does not interpret it.
        /// </summary>
        public string Kind { get; }

        /// <summary>Element.UniqueId of each sheet/view in the selection.</summary>
        public IReadOnlyList<string> UniqueIds { get; }
    }

    /// <summary>
    /// Per-project memory of named selection filters, so a user who exports the same subset of
    /// sheets/views repeatedly (one discipline, one milestone...) only has to mark the checkboxes
    /// once.
    /// </summary>
    public interface ISavedSelectionStore
    {
        /// <summary>Returns the saved filters for <paramref name="projectKey"/>, or empty. Never throws.</summary>
        IReadOnlyList<SavedSelection> Load(string projectKey);

        /// <summary>Persists the full list of filters for <paramref name="projectKey"/>. Never throws.</summary>
        void Save(string projectKey, IReadOnlyList<SavedSelection> selections);
    }
}
