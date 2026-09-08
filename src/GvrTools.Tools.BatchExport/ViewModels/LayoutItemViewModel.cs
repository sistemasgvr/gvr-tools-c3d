using System;
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
