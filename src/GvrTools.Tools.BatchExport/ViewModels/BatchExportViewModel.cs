using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using GvrTools.Civil3D.Export;
using GvrTools.Civil3D.Export.Pdf;
using GvrTools.Civil3D.Infrastructure;
using GvrTools.Civil3D.Layouts;
using GvrTools.Civil3D.Model;
using GvrTools.Core.Batch;
using GvrTools.Core.Diagnostics;
using GvrTools.Core.History;
using GvrTools.Core.IO;
using GvrTools.Core.Settings;
using GvrTools.UI.Mvvm;
using GvrTools.UI.Services;

namespace GvrTools.Tools.BatchExport.ViewModels
{
    /// <summary>
    /// Window logic for the batch layout exporter.
    ///
    /// Much smaller than Revit's <c>BatchExportViewModel</c> on purpose: this MVP exports layouts
    /// of the active drawing to PDF only (no DWG-per-layout, no saved selection filters, no
    /// printer-driver special-casing — AutoCAD's own "DWG To PDF.pc3" device works unattended on
    /// every supported release, unlike Revit 2021's missing PDF API). Those Revit-only complexities
    /// are exactly what this rewrite intentionally leaves out; see IExportEngine for how a DWG
    /// engine would plug back in later.
    /// </summary>
    public sealed class BatchExportViewModel : ObservableObject, IDisposable
    {
        private const string DialogTitle = "GVR Tools - Exportación masiva";

        private readonly Document _document;
        private readonly CivilJobScheduler _scheduler;
        private readonly IUserDialogs _dialogs;
        private readonly ISettingsStore _settingsStore;
        private readonly ISheetExportHistoryStore _historyStore;
        private readonly ILog _log;
        private readonly ExportEngineCatalog _engines;
        private readonly DrawingSnapshot _drawing;
        private readonly Dictionary<string, DateTime> _exportHistory;

        private string _runDestinationFolder;

        public BatchExportViewModel(
            Document document,
            CivilJobScheduler scheduler,
            IUserDialogs dialogs,
            ISettingsStore settingsStore,
            ISheetExportHistoryStore historyStore,
            ILog log)
        {
            _document = document;
            _scheduler = scheduler;
            _dialogs = dialogs;
            _settingsStore = settingsStore;
            _historyStore = historyStore ?? new SheetExportHistoryStore();
            _log = log ?? NullLog.Instance;
            _engines = ExportEngineCatalog.CreateDefault();

            _drawing = DrawingSnapshot.Read(document.Name);

            _exportHistory = new Dictionary<string, DateTime>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, DateTime> entry in _historyStore.Load(_drawing.DrawingKey))
                _exportHistory[entry.Key] = entry.Value;

            LoadLayouts();

            // Los comandos deben existir ANTES de aplicar las preferencias: al asignar OutputFolder
            // su setter llama a ExportCommand.RaiseCanExecuteChanged(), y si ApplyPreferences corre
            // primero ExportCommand aún es null → NullReferenceException al abrir la ventana.
            SelectAllCommand = new RelayCommand(() => SetSelection(true));
            SelectNoneCommand = new RelayCommand(() => SetSelection(false));
            BrowseFolderCommand = new RelayCommand(BrowseFolder);
            OpenFolderCommand = new RelayCommand(() => _dialogs.Reveal(DestinationFolder));
            ExportCommand = new RelayCommand(StartExport, () => CanExport);
            CancelCommand = new RelayCommand(RequestCancel, () => IsExporting);

            BatchExportPreferences preferences = _settingsStore.Load<BatchExportPreferences>(BatchExportPreferences.StorageKey);
            ApplyPreferences(preferences);

            StatusText = Layouts.Count == 0
                ? "El dibujo activo no tiene presentaciones para exportar."
                : $"{Layouts.Count} presentación(es) en el dibujo.";
        }

        // ---------------------------------------------------------------- layout list

        public ObservableCollection<LayoutItemViewModel> Layouts { get; } = new ObservableCollection<LayoutItemViewModel>();

        public ObservableCollection<ExportResultItemViewModel> Results { get; } = new ObservableCollection<ExportResultItemViewModel>();

        public string DocumentTitle => _drawing.Title;

        /// <summary>Exposed so the command can detect the user switched active documents before reactivating an open window.</summary>
        public Document Document => _document;

        private void LoadLayouts()
        {
            Layouts.Clear();

            IReadOnlyList<LayoutSnapshot> snapshots = LayoutRepository.GetLayouts(_document.Database);

            foreach (LayoutSnapshot snapshot in snapshots)
            {
                var item = new LayoutItemViewModel(snapshot);
                if (_exportHistory.TryGetValue(snapshot.ObjectIdHandle, out DateTime lastExported))
                    item.LastExportedUtc = lastExported;

                item.PropertyChanged += (s, e) => Raise(nameof(SelectionSummary), nameof(CanExport));
                Layouts.Add(item);
            }
        }

        public int SelectedCount => Layouts.Count(item => item.IsSelected);

        public string SelectionSummary => $"{SelectedCount} de {Layouts.Count} presentaciones seleccionadas";

        private void SetSelection(bool selected)
        {
            foreach (LayoutItemViewModel item in Layouts) item.IsSelected = selected;
            Raise(nameof(SelectionSummary), nameof(CanExport));
        }

        // ---------------------------------------------------------------- destination and naming

        private string _outputFolder = string.Empty;
        public string OutputFolder
        {
            get => _outputFolder;
            set
            {
                if (!Set(ref _outputFolder, value)) return;
                Raise(nameof(DestinationFolder), nameof(CanExport));
                ExportCommand?.RaiseCanExecuteChanged();
            }
        }

        /// <summary>Each run writes into a subfolder named after the drawing file.</summary>
        public string DestinationFolder
        {
            get
            {
                if (!string.IsNullOrEmpty(_runDestinationFolder)) return _runDestinationFolder;
                if (string.IsNullOrWhiteSpace(OutputFolder)) return string.Empty;

                string desired = System.IO.Path.Combine(OutputFolder, "PDF_" + _drawing.Title);
                return ExportPathHelper.AllocateUniqueDirectoryPath(desired);
            }
        }

        public string NamingHelpText => NamingTokens.HelpText;

        private string _namingPattern = NamingTokens.DefaultPattern;
        public string NamingPattern
        {
            get => _namingPattern;
            set
            {
                if (!Set(ref _namingPattern, value)) return;
                Raise(nameof(NamingPreview));
            }
        }

        public string NamingPreview
        {
            get
            {
                LayoutSnapshot sample = Layouts.Count > 0 ? Layouts[0].Layout : null;
                if (sample == null) return "(sin presentaciones para previsualizar)";

                var namer = new ExportFileNamer(string.Empty, NamingPattern, string.Empty, _drawing.ToTokens());
                return namer.Preview(sample);
            }
        }

        // ---------------------------------------------------------------- PDF options

        private bool _useLayoutPageSetup = true;
        public bool UseLayoutPageSetup
        {
            get => _useLayoutPageSetup;
            set
            {
                if (Set(ref _useLayoutPageSetup, value))
                    Raise(nameof(ShowPlotDevice));
            }
        }

        public bool ShowPlotDevice => !UseLayoutPageSetup;

        private string _plotDeviceName = "DWG To PDF.pc3";
        public string PlotDeviceName
        {
            get => _plotDeviceName;
            set => Set(ref _plotDeviceName, value ?? string.Empty);
        }

        private bool _fitToPaper = true;
        public bool FitToPaper
        {
            get => _fitToPaper;
            set => Set(ref _fitToPaper, value);
        }

        private bool _combineIntoSinglePdf;
        public bool CombineIntoSinglePdf
        {
            get => _combineIntoSinglePdf;
            set => Set(ref _combineIntoSinglePdf, value);
        }

        private bool _openFolderWhenDone = true;
        public bool OpenFolderWhenDone
        {
            get => _openFolderWhenDone;
            set => Set(ref _openFolderWhenDone, value);
        }

        public string StrategyDescription => _engines.Resolve(ExportFormat.Pdf).StrategyDescription;

        // ---------------------------------------------------------------- run state

        private bool _isExporting;
        public bool IsExporting
        {
            get => _isExporting;
            set
            {
                if (!Set(ref _isExporting, value)) return;
                Raise(nameof(CanEditOptions));
                ExportCommand.RaiseCanExecuteChanged();
                CancelCommand.RaiseCanExecuteChanged();
            }
        }

        public bool CanEditOptions => !IsExporting;

        private int _progressValue;
        public int ProgressValue
        {
            get => _progressValue;
            set => Set(ref _progressValue, value);
        }

        private int _progressMaximum = 1;
        public int ProgressMaximum
        {
            get => _progressMaximum;
            set => Set(ref _progressMaximum, value);
        }

        private string _statusText = string.Empty;
        public string StatusText
        {
            get => _statusText;
            set => Set(ref _statusText, value);
        }

        private bool _showResults;
        public bool ShowResults
        {
            get => _showResults;
            set => Set(ref _showResults, value);
        }

        public bool CanExport =>
            !IsExporting &&
            SelectedCount > 0 &&
            !string.IsNullOrWhiteSpace(OutputFolder);

        // ---------------------------------------------------------------- commands

        public RelayCommand SelectAllCommand { get; }

        public RelayCommand SelectNoneCommand { get; }

        public RelayCommand BrowseFolderCommand { get; }

        public RelayCommand OpenFolderCommand { get; }

        public RelayCommand ExportCommand { get; }

        public RelayCommand CancelCommand { get; }

        private void BrowseFolder()
        {
            string picked = _dialogs.PickFolder("Elige la carpeta de destino", OutputFolder);
            if (picked != null) OutputFolder = picked;
        }

        private void StartExport()
        {
            if (!CanExport) return;

            List<LayoutSnapshot> selected = Layouts.Where(item => item.IsSelected).Select(item => item.Layout).ToList();
            if (selected.Count == 0) return;

            _runDestinationFolder = DestinationFolder;
            Raise(nameof(DestinationFolder));

            var settings = new PdfExportSettings
            {
                UseLayoutPageSetup = UseLayoutPageSetup,
                PlotDeviceName = PlotDeviceName,
                FitToPaper = FitToPaper,
                CombineIntoSinglePdf = CombineIntoSinglePdf
            };

            var request = new ExportRequest(_document.Database, _runDestinationFolder, NamingPattern, settings, _drawing, _log, _document);

            IExportEngine engine = _engines.Resolve(ExportFormat.Pdf);

            Results.Clear();
            ShowResults = false;
            ProgressValue = 0;
            ProgressMaximum = selected.Count;
            IsExporting = true;
            StatusText = "Exportando...";

            var job = new BatchExportJob(
                engine,
                request,
                selected,
                progress =>
                {
                    ProgressValue = progress.Completed;
                    ProgressMaximum = progress.Total;
                    if (progress.Completed < progress.Total)
                        StatusText = $"Exportando \"{progress.CurrentLabel}\"...";
                },
                result => Results.Add(new ExportResultItemViewModel(result)),
                OnFinished);

            try
            {
                SavePreferences();
                _scheduler.Start(job);
            }
            catch (Exception ex)
            {
                IsExporting = false;
                _log.Error("No se pudo iniciar la exportación.", ex);
                _dialogs.ShowError(DialogTitle, ex.Message);
            }
        }

        private void RequestCancel() => _scheduler.RequestCancel();

        private void OnFinished(BatchResult result)
        {
            IsExporting = false;
            ShowResults = true;

            if (result.HasSetupError)
            {
                StatusText = "No se pudo exportar: " + result.SetupError;
                _dialogs.ShowError(DialogTitle, result.SetupError);
                return;
            }

            StatusText = result.WasCancelled
                ? $"Cancelado. {result.SucceededCount} exportada(s) antes de cancelar."
                : $"Listo: {result.SucceededCount} correcta(s), {result.FailedCount} con error.";

            DateTime now = DateTime.UtcNow;
            foreach (BatchItemResult item in result.Items)
            {
                if (!item.Succeeded) continue;

                LayoutItemViewModel match = Layouts.FirstOrDefault(l => l.Name == item.Label);
                if (match != null)
                {
                    match.LastExportedUtc = now;
                    _exportHistory[match.Layout.ObjectIdHandle] = now;
                }
            }

            _historyStore.Save(_drawing.DrawingKey, _exportHistory);

            if (OpenFolderWhenDone && result.SucceededCount > 0)
                _dialogs.Reveal(result.DestinationFolder);

            _runDestinationFolder = null;
        }

        private void ApplyPreferences(BatchExportPreferences preferences)
        {
            OutputFolder = preferences.OutputFolder;
            NamingPattern = preferences.NamingPattern;
            OpenFolderWhenDone = preferences.OpenFolderWhenDone;
            UseLayoutPageSetup = preferences.PdfUseLayoutPageSetup;
            PlotDeviceName = preferences.PdfPlotDeviceName;
            FitToPaper = preferences.PdfFitToPaper;
            CombineIntoSinglePdf = preferences.PdfCombineIntoSinglePdf;
        }

        private void SavePreferences()
        {
            _settingsStore.Save(BatchExportPreferences.StorageKey, new BatchExportPreferences
            {
                OutputFolder = OutputFolder,
                NamingPattern = NamingPattern,
                OpenFolderWhenDone = OpenFolderWhenDone,
                PdfUseLayoutPageSetup = UseLayoutPageSetup,
                PdfPlotDeviceName = PlotDeviceName,
                PdfFitToPaper = FitToPaper,
                PdfCombineIntoSinglePdf = CombineIntoSinglePdf
            });
        }

        public void Dispose()
        {
        }
    }
}
