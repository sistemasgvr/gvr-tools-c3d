using System.Collections.Generic;

namespace GvrTools.Civil3D.Model
{
    /// <summary>
    /// Plain, immutable copy of the layout (presentación) data the UI and the exporter need, read
    /// once while the drawing database is safe to touch.
    ///
    /// Equivalent to Revit's <c>SheetSnapshot</c>: a layout is the closest Civil 3D concept to a
    /// Revit sheet — a named plot-ready page inside the drawing, with its own page setup and
    /// plottable area.
    ///
    /// Only the object id (as a string handle) is kept, never the live <c>Layout</c>/entity: those
    /// can go stale between opening the window and pressing Export, and re-resolving from the
    /// handle at export time turns that into a clean per-item error instead of a low-level
    /// exception from inside the database.
    /// </summary>
    public sealed class LayoutSnapshot
    {
        public LayoutSnapshot(string objectIdHandle, string name, int tabOrder, string pageSetupName, string plotStyleTable = null)
        {
            ObjectIdHandle = objectIdHandle ?? string.Empty;
            Name = name ?? string.Empty;
            TabOrder = tabOrder;
            PageSetupName = pageSetupName ?? string.Empty;
            PlotStyleTable = plotStyleTable ?? string.Empty;
        }

        /// <summary>
        /// AutoCAD entity handle of the <c>Layout</c> object, stable across sessions unlike a raw
        /// <c>ObjectId</c> — this is the key the export-history store uses to remember "was this
        /// layout exported before" from one session to the next.
        /// </summary>
        public string ObjectIdHandle { get; }

        /// <summary>Layout (tab) name, e.g. "Planta general" or "Perfil km 0+000".</summary>
        public string Name { get; }

        /// <summary>Left-to-right order of the tab strip; used for natural, non-alphabetical ordering.</summary>
        public int TabOrder { get; }

        /// <summary>Named page setup assigned to the layout, if any.</summary>
        public string PageSetupName { get; }

        /// <summary>
        /// Plot style table (.ctb/.stb) the layout's page setup points at. May name a table that is
        /// not installed on this machine — the Plot dialog renders that as "(missing)" — so it says
        /// what the layout wants, not that it can be loaded.
        /// </summary>
        public string PlotStyleTable { get; }

        public string Label => Name;

        /// <summary>Values for the layout-scoped file-name tokens.</summary>
        public IReadOnlyDictionary<string, string> ToTokens() => new Dictionary<string, string>
        {
            ["LayoutName"] = Name,
            ["PageSetupName"] = PageSetupName
        };
    }
}
