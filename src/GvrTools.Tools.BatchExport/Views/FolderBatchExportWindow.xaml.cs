using System;
using System.Windows;
using GvrTools.Tools.BatchExport.ViewModels;

namespace GvrTools.Tools.BatchExport.Views
{
    /// <summary>Modeless window hosting <see cref="FolderBatchExportViewModel"/>.</summary>
    public partial class FolderBatchExportWindow : Window
    {
        private readonly FolderBatchExportViewModel _viewModel;
        private readonly Action _onClosed;

        public FolderBatchExportWindow(FolderBatchExportViewModel viewModel, Action onClosed)
        {
            InitializeComponent();

            _viewModel = viewModel;
            _onClosed = onClosed;
            DataContext = _viewModel;

            Closed += (s, e) =>
            {
                _viewModel.Dispose();
                _onClosed?.Invoke();
            };
        }
    }
}
