using System;
using GvrTools.Civil3D.Export;
using GvrTools.Civil3D.Model;
using GvrTools.UI.Mvvm;

namespace GvrTools.Tools.BatchExport.ViewModels
{
    /// <summary>One row of the layout grid.</summary>
    public sealed class LayoutItemViewModel : ObservableObject
    {
        public LayoutItemViewModel(LayoutSnapshot layout)
        {
            Layout = layout;
        }

        public LayoutSnapshot Layout { get; }

        public string Name => Layout.Name;

        public string PageSetupName => Layout.PageSetupName;

        /// <summary>
        /// Plot style table of the layout, flagged the way AutoCAD's own Plot dialog does when the
        /// table it names is not installed on this machine.
        /// </summary>
        public string PlotStyleTableLabel
        {
            get
            {
                string table = Layout.PlotStyleTable;
                if (string.IsNullOrWhiteSpace(table)) return "(ninguna)";

                return PlotStyleTableRepository.IsInstalled(table) ? table : table + " (falta)";
            }
        }

        private bool _isSelected = true;
        public bool IsSelected
        {
            get => _isSelected;
            set => Set(ref _isSelected, value);
        }

        public DateTime? LastExportedUtc { get; set; }

        public string LastExportedLabel => LastExportedUtc.HasValue
            ? LastExportedUtc.Value.ToLocalTime().ToString("g")
            : "Nunca";
    }
}
