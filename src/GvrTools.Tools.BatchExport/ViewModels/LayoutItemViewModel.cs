using System;
using GvrTools.Civil3D.Export;
using GvrTools.Civil3D.Model;
using GvrTools.Core.IO;
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

        /// <summary>Device of the layout's own page setup ("Ninguno" when it has none).</summary>
        public string PlotDeviceLabel => PlotDeviceRepository.IsNoneDevice(Layout.PlotDeviceName)
            ? "Ninguno"
            : Layout.PlotDeviceName;

        /// <summary>Paper of the layout's own page setup, readable.</summary>
        public string PaperLabel => PlotMediaNames.Humanize(Layout.CanonicalMediaName);

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

        // Sin marcar al abrir: el primer paso del flujo es elegir qué se traza.
        private bool _isSelected;
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
