using System;
using System.Windows;
using Autodesk.AutoCAD.ApplicationServices;
using GvrTools.Tools.BatchExport.ViewModels;

namespace GvrTools.Tools.BatchExport.Views
{
    /// <summary>Modeless window hosting <see cref="BatchExportViewModel"/>.</summary>
    public partial class BatchExportWindow : Window
    {
        private readonly BatchExportViewModel _viewModel;
        private readonly Action _onClosed;

        public BatchExportWindow(BatchExportViewModel viewModel, Action onClosed)
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

        public Document Document => _viewModel.Document;

        public string DocumentTitle => _viewModel.DocumentTitle;
    }
}
