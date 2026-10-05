using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Windows;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using GvrTools.Civil3D.Export;
using GvrTools.Civil3D.Export.Plotting;
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
    /// Window logic for the batch layout plotter: which layouts (tab "Presentaciones"), how to plot
    /// them (tab "Trazado", see <see cref="PlotConfigurationViewModel"/>) and where the output goes
    /// (tab "Salida").
    ///
    /// Much smaller than Revit's <c>BatchExportViewModel</c> on purpose: AutoCAD's own plot engine
    /// drives every device of the Plot dialog unattended on every supported release, so there is
    /// no printer-driver special-casing here; see IExportEngine for how other engines plug in.
    /// </summary>
    public sealed class BatchExportViewModel : ObservableObject, IDisposable
    {
        private const string DialogTitle = "GVR Tools - Trazado masivo";
        private const string PlotSettingsKey = BatchExportPreferences.StorageKey + "-plot";

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
        private bool _runWritesFiles;
        private Window _hostWindow;

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

            // Los comandos deben existir ANTES de aplicar las preferencias: al asignar OutputFolder
            // su setter llama a ExportCommand.RaiseCanExecuteChanged(), y si ApplyPreferences corre
            // primero ExportCommand aún es null → NullReferenceException al abrir la ventana.
            SelectAllCommand = new RelayCommand(() => SetSelection(true));
            SelectNoneCommand = new RelayCommand(() => SetSelection(false));
            BrowseFolderCommand = new RelayCommand(BrowseFolder);
            OpenFolderCommand = new RelayCommand(() => _dialogs.Reveal(DestinationFolder), () => !string.IsNullOrEmpty(DestinationFolder));
            PreviewCommand = new RelayCommand(Preview, () => CanPreview);
            ExportCommand = new RelayCommand(StartExport, () => CanExport);
            CancelCommand = new RelayCommand(RequestCancel, () => IsExporting);

            Wizard = new WizardStepsViewModel(
                step => step == 0 ? SelectedCount > 0 : step != 1 || (Plot != null && Plot.IsValid),
                new[]
                {
                    "marca las presentaciones a trazar y pulsa Siguiente.",
                    "revisa la configuración de trazado y pulsa Siguiente.",
                    "elige la carpeta y el nombre de los archivos, y pulsa Trazar."
                });
            Wizard.PropertyChanged += (s, e) =>
            {
                Raise(nameof(CanExport));
                ExportCommand.RaiseCanExecuteChanged();
            };

            LoadLayouts();

            Plot = new PlotConfigurationViewModel(
                _dialogs,
                _log,
                DialogTitle,
                PlotScaleRepository.GetScales(document.Database),
                ReadPageSetups(document.Database),
                () => _drawing.LocalFolder)
            {
                MissingPlotStyleTablesProvider = GetMissingPlotStyleTables,
                SelectedLayoutsProvider = () => Layouts.Where(l => l.IsSelected).Select(l => l.Layout).ToList(),
                PickWindowHandler = PickCommonWindow
            };
            Plot.SettingsChanged += (s, e) => OnPlotSettingsChanged();

            ApplyPreferences(_settingsStore.Load<BatchExportPreferences>(BatchExportPreferences.StorageKey));
            Plot.Apply(LegacyPdfPreferences.LoadPlotSettings(_settingsStore, BatchExportPreferences.StorageKey, PlotSettingsKey));

            PreviewLayout = Layouts.FirstOrDefault();
            Wizard.Refresh();

            StatusText = Layouts.Count == 0
                ? "El dibujo activo no tiene presentaciones para trazar."
                : $"{Layouts.Count} presentación(es) en el dibujo.";
        }

        /// <summary>The plot options (tab "Trazado").</summary>
        public PlotConfigurationViewModel Plot { get; }

        /// <summary>Presentaciones → Trazado → Salida; "Trazar" only once all three were visited.</summary>
        public WizardStepsViewModel Wizard { get; }

        /// <summary>
        /// Lets the view model hide its window while AutoCAD shows the plot preview or asks for a
        /// window pick, as the Plot dialog itself does.
        /// </summary>
        public void AttachWindow(Window window) => _hostWindow = window;

        // ---------------------------------------------------------------- layout list

        public ObservableCollection<LayoutItemViewModel> Layouts { get; } = new ObservableCollection<LayoutItemViewModel>();

        public ObservableCollection<ExportResultItemViewModel> Results { get; } = new ObservableCollection<ExportResultItemViewModel>();

        public string DocumentTitle => _drawing.Title;

        /// <summary>Exposed so the command can detect the user switched active documents before reactivating an open window.</summary>
        public Document Document => _document;

        private void LoadLayouts()
        {
            Layouts.Clear();

            foreach (LayoutSnapshot snapshot in LayoutRepository.GetLayouts(_document.Database))
            {
                var item = new LayoutItemViewModel(snapshot);
                if (_exportHistory.TryGetValue(snapshot.ObjectIdHandle, out DateTime lastExported))
                    item.LastExportedUtc = lastExported;

                item.PropertyChanged += (s, e) => OnSelectionChanged();
                Layouts.Add(item);
            }
        }

        public int SelectedCount => Layouts.Count(item => item.IsSelected);

        public string SelectionSummary => $"{SelectedCount} de {Layouts.Count} presentaciones seleccionadas";

        private void SetSelection(bool selected)
        {
            foreach (LayoutItemViewModel item in Layouts) item.IsSelected = selected;
            OnSelectionChanged();
        }

        private void OnSelectionChanged()
        {
            // La presentación de referencia sigue a la selección si la actual ya no se va a trazar.
            if (PreviewLayout == null || !PreviewLayout.IsSelected)
            {
                LayoutItemViewModel firstSelected = Layouts.FirstOrDefault(l => l.IsSelected);
                if (firstSelected != null) PreviewLayout = firstSelected;
            }

            Raise(nameof(SelectionSummary), nameof(CanExport));
            Plot?.RefreshLayoutWarnings();
            Wizard?.Refresh();
            ExportCommand?.RaiseCanExecuteChanged();
        }

        private IReadOnlyList<string> GetMissingPlotStyleTables()
        {
            var missing = new List<string>();

            foreach (LayoutItemViewModel item in Layouts)
            {
                if (!item.IsSelected) continue;

                string table = item.Layout.PlotStyleTable;
                if (string.IsNullOrWhiteSpace(table)) continue;
                if (PlotStyleTableRepository.IsInstalled(table)) continue;
                if (!missing.Contains(table)) missing.Add(table);
            }

            return missing;
        }

        private IReadOnlyList<NamedPageSetup> ReadPageSetups(Database database)
        {
            try
            {
                return PageSetupRepository.Read(database);
            }
            catch (Exception ex)
            {
                _log.Warn("No se pudieron leer las configuraciones de página del dibujo: " + ex.Message);
                return Array.Empty<NamedPageSetup>();
            }
        }

        // ---------------------------------------------------------------- destination and naming

        private string _outputFolder = string.Empty;
        public string OutputFolder
        {
            get => _outputFolder;
            set
            {
                if (!Set(ref _outputFolder, value)) return;
                Raise(nameof(DestinationFolder), nameof(DestinationSummary), nameof(CanExport));
                ExportCommand?.RaiseCanExecuteChanged();
                OpenFolderCommand?.RaiseCanExecuteChanged();
            }
        }

        /// <summary>
        /// Each run writes into a subfolder named after the output and the drawing ("PDF_Planos");
        /// empty when the run only prints.
        /// </summary>
        public string DestinationFolder
        {
            get
            {
                if (!string.IsNullOrEmpty(_runDestinationFolder)) return _runDestinationFolder;
                if (Plot == null || !Plot.WritesFiles || string.IsNullOrWhiteSpace(OutputFolder)) return string.Empty;

                string desired = System.IO.Path.Combine(OutputFolder, Plot.OutputFolderPrefix + "_" + _drawing.Title);
                return ExportPathHelper.AllocateUniqueDirectoryPath(desired);
            }
        }

        public string DestinationSummary
        {
            get
            {
                if (Plot != null && !Plot.WritesFiles)
                    return "Impresión directa: no se crean archivos ni carpetas.";

                return string.IsNullOrWhiteSpace(OutputFolder)
                    ? "Elige la carpeta donde se guardarán los archivos."
                    : "Se creará: " + DestinationFolder;
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

        private bool _openFolderWhenDone = true;
        public bool OpenFolderWhenDone
        {
            get => _openFolderWhenDone;
            set => Set(ref _openFolderWhenDone, value);
        }

        private void BrowseFolder()
        {
            string picked = _dialogs.PickFolder("Elige la carpeta de destino", OutputFolder);
            if (picked != null) OutputFolder = picked;
        }

        private void OnPlotSettingsChanged()
        {
            Wizard?.Refresh();
            Raise(nameof(DestinationFolder), nameof(DestinationSummary), nameof(CanExport), nameof(CanPreview));
            ExportCommand?.RaiseCanExecuteChanged();
            PreviewCommand?.RaiseCanExecuteChanged();
            OpenFolderCommand?.RaiseCanExecuteChanged();
        }

        // ---------------------------------------------------------------- preview and window pick

        private LayoutItemViewModel _previewLayout;
        /// <summary>Layout used by "Vista preliminar" and "Designar ventana".</summary>
        public LayoutItemViewModel PreviewLayout
        {
            get => _previewLayout;
            set
            {
                if (!Set(ref _previewLayout, value)) return;
                Raise(nameof(CanPreview));
                PreviewCommand?.RaiseCanExecuteChanged();
            }
        }

        public bool CanPreview => !IsExporting && PreviewLayout != null && Plot != null && Plot.IsValid;

        private void Preview()
        {
            if (!CanPreview) return;

            string error = null;
            PlotExportSettings settings = Plot.BuildSettings();
            RunWhileHidden(() => error = PlotPreviewService.ShowPreview(_document, PreviewLayout.Layout, settings, _log));

            if (error != null) _dialogs.ShowWarning(DialogTitle, error);
        }

        private Extents2d? PickCommonWindow()
        {
            LayoutItemViewModel reference = PreviewLayout
                ?? Layouts.FirstOrDefault(l => l.IsSelected)
                ?? Layouts.FirstOrDefault();

            if (reference == null || _hostWindow == null) return null;

            try
            {
                return PlotPreviewService.TryPickWindow(_document, reference.Name, _hostWindow, _log, out Extents2d window)
                    ? window
                    : (Extents2d?)null;
            }
            catch (Exception ex)
            {
                _log.Error("No se pudo designar la ventana de trazado.", ex);
                _dialogs.ShowError(DialogTitle, "No se pudo designar la ventana: " + ex.Message);
                return null;
            }
        }

        private void RunWhileHidden(Action body)
        {
            Window window = _hostWindow;
            if (window == null)
            {
                body();
                return;
            }

            window.Hide();
            try
            {
                body();
            }
            finally
            {
                window.Show();
                window.Activate();
            }
        }

        // ---------------------------------------------------------------- run state

        private bool _isExporting;
        public bool IsExporting
        {
            get => _isExporting;
            set
            {
                if (!Set(ref _isExporting, value)) return;
                Raise(nameof(CanEditOptions), nameof(CanPreview));
                ExportCommand.RaiseCanExecuteChanged();
                CancelCommand.RaiseCanExecuteChanged();
                PreviewCommand.RaiseCanExecuteChanged();
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
            Wizard != null && Wizard.HasReachedLastStep &&
            SelectedCount > 0 &&
            Plot != null && Plot.IsValid &&
            (!Plot.WritesFiles || !string.IsNullOrWhiteSpace(OutputFolder));

        // ---------------------------------------------------------------- commands

        public RelayCommand SelectAllCommand { get; }

        public RelayCommand SelectNoneCommand { get; }

        public RelayCommand BrowseFolderCommand { get; }

        public RelayCommand OpenFolderCommand { get; }

        public RelayCommand PreviewCommand { get; }

        public RelayCommand ExportCommand { get; }

        public RelayCommand CancelCommand { get; }

        private void StartExport()
        {
            if (!CanExport) return;

            List<LayoutSnapshot> selected = Layouts.Where(item => item.IsSelected).Select(item => item.Layout).ToList();
            if (selected.Count == 0) return;

            if (Plot.HasClipWarning &&
                !_dialogs.Confirm(DialogTitle, Plot.ClipWarning + Environment.NewLine + Environment.NewLine + "¿Trazar de todos modos?"))
            {
                Wizard.SelectedIndex = 1;
                return;
            }

            _runWritesFiles = Plot.WritesFiles;
            _runDestinationFolder = DestinationFolder;
            Raise(nameof(DestinationFolder), nameof(DestinationSummary));

            PlotExportSettings settings = Plot.BuildSettings();
            var request = new ExportRequest(_document.Database, _runDestinationFolder, NamingPattern, settings, _drawing, _log, _document);

            IExportEngine engine = _engines.Resolve(ExportFormat.Plot);

            Results.Clear();
            ShowResults = false;
            ProgressValue = 0;
            ProgressMaximum = selected.Count;
            IsExporting = true;
            StatusText = "Trazando...";

            var job = new BatchExportJob(
                engine,
                request,
                selected,
                progress =>
                {
                    ProgressValue = progress.Completed;
                    ProgressMaximum = progress.Total;
                    if (progress.Completed < progress.Total)
                        StatusText = $"Trazando \"{progress.CurrentLabel}\"...";
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
                _log.Error("No se pudo iniciar el trazado.", ex);
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
                StatusText = "No se pudo trazar: " + result.SetupError;
                _dialogs.ShowError(DialogTitle, result.SetupError);
                _runDestinationFolder = null;
                return;
            }

            StatusText = result.WasCancelled
                ? $"Cancelado. {result.SucceededCount} trazada(s) antes de cancelar."
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

            string destination = result.DestinationFolder;

            if (OpenFolderWhenDone && _runWritesFiles && result.SucceededCount > 0)
                _dialogs.Reveal(destination);

            ShowCompletionDialog(result, destination);

            _runDestinationFolder = null;
            Raise(nameof(DestinationFolder), nameof(DestinationSummary));
        }

        /// <summary>
        /// Tells the user the run is over. The window stays open behind it (results grid, folder
        /// button), so this only has to answer "did it finish, and did anything fail".
        /// </summary>
        private void ShowCompletionDialog(BatchResult result, string destinationFolder)
        {
            if (result.WasCancelled)
            {
                _dialogs.ShowWarning(DialogTitle,
                    $"Trazado cancelado.{Environment.NewLine}{Environment.NewLine}" +
                    $"Se alcanzaron a trazar {result.SucceededCount} presentación(es).");
                return;
            }

            var message = new StringBuilder();
            message.AppendLine($"Se trazaron {result.SucceededCount} presentación(es).");

            if (_runWritesFiles && !string.IsNullOrEmpty(destinationFolder))
            {
                message.AppendLine();
                message.AppendLine("Carpeta:");
                message.Append(destinationFolder);
            }

            if (result.FailedCount == 0)
            {
                _dialogs.ShowInfo(DialogTitle, message.ToString());
                return;
            }

            message.AppendLine();
            message.AppendLine();
            message.AppendLine($"{result.FailedCount} con error:");

            const int MaxListed = 5;
            int listed = 0;
            foreach (BatchItemResult failure in result.Failures)
            {
                if (listed == MaxListed)
                {
                    message.Append($"  ... y {result.FailedCount - MaxListed} más (ver la lista de resultados).");
                    break;
                }

                message.AppendLine($"  • {failure.Label}: {failure.Message}");
                listed++;
            }

            _dialogs.ShowWarning(DialogTitle, message.ToString());
        }

        private void ApplyPreferences(BatchExportPreferences preferences)
        {
            OutputFolder = preferences.OutputFolder;
            NamingPattern = string.IsNullOrWhiteSpace(preferences.NamingPattern) ? NamingTokens.DefaultPattern : preferences.NamingPattern;
            OpenFolderWhenDone = preferences.OpenFolderWhenDone;
        }

        private void SavePreferences()
        {
            _settingsStore.Save(BatchExportPreferences.StorageKey, new BatchExportPreferences
            {
                OutputFolder = OutputFolder,
                NamingPattern = NamingPattern,
                OpenFolderWhenDone = OpenFolderWhenDone
            });

            _settingsStore.Save(PlotSettingsKey, Plot.BuildSettings());
        }

        public void Dispose()
        {
        }
    }
}
