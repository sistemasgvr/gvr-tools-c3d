using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using GvrTools.Civil3D.Model;
using GvrTools.Core.Text;

namespace GvrTools.Civil3D.Layouts
{
    /// <summary>
    /// Read-only access to the plottable layouts of a drawing. Equivalent to Revit's
    /// <c>SheetRepository</c>, built on <c>LayoutManager</c> / <c>DBDictionary</c> instead of
    /// <c>FilteredElementCollector</c>.
    /// </summary>
    public static class LayoutRepository
    {
        /// <summary>
        /// All plottable layouts (Model space excluded), ordered the same way the layout tab strip
        /// shows them.
        /// </summary>
        public static IReadOnlyList<LayoutSnapshot> GetLayouts(Database database)
        {
            var result = new List<LayoutSnapshot>();

            using (var tr = database.TransactionManager.StartTransaction())
            {
                var layoutDictionary = (DBDictionary)tr.GetObject(database.LayoutDictionaryId, OpenMode.ForRead);

                foreach (DBDictionaryEntry entry in layoutDictionary)
                {
                    var layout = (Layout)tr.GetObject(entry.Value, OpenMode.ForRead);
                    if (layout.ModelType) continue; // "Model" tab, not a real layout to export.

                    result.Add(new LayoutSnapshot(
                        layout.ObjectId.Handle.ToString(),
                        layout.LayoutName,
                        layout.TabOrder,
                        SafePageSetupName(layout),
                        SafePlotStyleTable(layout)));
                }

                tr.Commit();
            }

            return result
                .OrderBy(layout => layout.TabOrder)
                .ThenBy(layout => layout.Name, NaturalSortComparer.Instance)
                .ToList();
        }

        /// <summary>
        /// Re-resolves a snapshot to the live <see cref="Layout"/> inside the given transaction.
        /// Returns null when the layout no longer exists, reported as a per-item failure.
        /// </summary>
        public static Layout Resolve(Database database, Transaction tr, LayoutSnapshot snapshot)
        {
            try
            {
                var handle = new Handle(long.Parse(snapshot.ObjectIdHandle, System.Globalization.NumberStyles.HexNumber));
                if (!database.TryGetObjectId(handle, out ObjectId id)) return null;

                return tr.GetObject(id, OpenMode.ForRead) as Layout;
            }
            catch (System.Exception)
            {
                return null;
            }
        }

        /// <summary>Plot style table (.ctb/.stb) the layout's page setup references, if any.</summary>
        private static string SafePlotStyleTable(Layout layout)
        {
            try
            {
                return layout.CurrentStyleSheet;
            }
            catch (System.Exception)
            {
                return string.Empty;
            }
        }

        private static string SafePageSetupName(Layout layout)
        {
            try
            {
                // PlotSettingsName is the name of the *named* page setup the layout points at (set via
                // Page Setup Manager); empty when the layout only has its own inline, unnamed settings.
                return string.IsNullOrEmpty(layout.PlotSettingsName)
                    ? layout.PlotConfigurationName
                    : layout.PlotSettingsName;
            }
            catch (System.Exception)
            {
                return string.Empty;
            }
        }
    }
}
