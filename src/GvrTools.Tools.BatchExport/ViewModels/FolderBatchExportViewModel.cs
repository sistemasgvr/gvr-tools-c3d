using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using GvrTools.Civil3D.Export;
using GvrTools.Civil3D.Export.Plotting;
using GvrTools.Civil3D.Infrastructure;
using GvrTools.Civil3D.Model;
using GvrTools.Core.Batch;
using GvrTools.Core.Diagnostics;
using GvrTools.Core.IO;
using GvrTools.Core.Settings;
using GvrTools.UI.Mvvm;
using GvrTools.UI.Services;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace GvrTools.Tools.BatchExport.ViewModels
{
    /// <summary>
    /// Window logic for the multi-drawing plotter: picks a source folder of .dwg files instead of
    /// a list of layouts from the active drawing, and hands each file to
    /// <see cref="FolderBatchExportJob"/>, which opens/plots/closes it without the user ever
    /// seeing it as a normal open drawing. Plot options come from the same
    /// <see cref="PlotConfigurationViewModel"/> as the active-drawing window.
    /// </summary>
    public sealed class FolderBatchExportViewModel : ObservableObject, IDisposable
    {
        private const string DialogTitle = "GVR Tools - Trazado masivo de dibujos";
        private const string PlotSettingsKey = FolderBatchExportPreferences.StorageKey + "-plot";

        private readonly CivilJobScheduler _scheduler;
        private readonly IUserDialogs _dialogs;
        private readonly ISettingsStore _settingsStore;
        private readonly ILog _log;

        private bool _runWritesFiles;

        public FolderBatchExportViewModel(
            CivilJobScheduler scheduler,
            IUserDialogs dialogs,
            ISettingsStore settingsStore,
            ILog log)
        {
            _scheduler = scheduler;
            _dialogs = dialogs;
            _settingsStore = settingsStore;
            _log = log ?? NullLog.Instance;

            SourceFolderCommand = new RelayCommand(BrowseSourceFolder);
            BrowseDestinationCommand = new RelayCommand(BrowseDestinationFolder);
            OpenFolderCommand = new RelayCommand(() => _dialogs.Reveal(DestinationFolder), () => !string.IsNullOrEmpty(DestinationFolder));
            ExportCommand = new RelayCommand(StartExport, () => CanExport);
            CancelCommand = new RelayCommand(RequestCancel, () => IsExporting);

            Wizard = new WizardStepsViewModel(
                step => step == 0 ? SourceFiles.Count > 0 : step != 1 || (Plot != null && Plot.IsValid),
                new[]
                {
                    "elige la carpeta con los dibujos y pulsa Siguiente.",
                    "revisa la configuración de trazado y pulsa Siguiente.",
                    "elige la carpeta de destino y el nombre de los archivos, y pulsa Trazar."
                });
            Wizard.PropertyChanged += (s, e) =>
            {
                Raise(nameof(CanExport));
                ExportCommand.RaiseCanExecuteChanged();
            };

            // Sin dibujos abiertos de la carpeta, la lista de escalas y las configuraciones de página
            // se toman del dibujo activo (si hay uno); siempre se pueden importar de un DWT.
            Database activeDatabase = AcApp.DocumentManager.MdiActiveDocument?.Database;
            Plot = new PlotConfigurationViewModel(
                _dialogs,
                _log,
                DialogTitle,
                PlotScaleRepository.GetScales(activeDatabase),
                ReadPageSetups(activeDatabase),
                () => SourceFolder);
            Plot.SettingsChanged += (s, e) => OnPlotSettingsChanged();

            FolderBatchExportPreferences preferences =
                _settingsStore.Load<FolderBatchExportPreferences>(FolderBatchExportPreferences.StorageKey);
            ApplyPreferences(preferences);

            PlotExportSettings plotSettings = LegacyPdfPreferences.LoadPlotSettings(
                _settingsStore, FolderBatchExportPreferences.StorageKey, PlotSettingsKey);
            plotSettings.CombineIntoSingleFile = false;
            Plot.Apply(plotSettings);

            RefreshSourceFiles();
        }

        /// <summary>The plot options (tab "Trazado").</summary>
        public PlotConfigurationViewModel Plot { get; }

        /// <summary>Dibujos → Trazado → Salida; "Trazar" only once all three were visited.</summary>
        public WizardStepsViewModel Wizard { get; }

        private IReadOnlyList<NamedPageSetup> ReadPageSetups(Database database)
        {
            if (database == null) return Array.Empty<NamedPageSetup>();

            try
            {
                return PageSetupRepository.Read(database);
            }
            catch (Exception ex)
            {
                _log.Warn("No se pudieron leer las configuraciones de página del dibujo activo: " + ex.Message);
                return Array.Empty<NamedPageSetup>();
            }
        }

        // ---------------------------------------------------------------- source folder

        private string _sourceFolder = string.Empty;
        public string SourceFolder
        {
            get => _sourceFolder;
            set
            {
                if (!Set(ref _sourceFolder, value)) return;
                RefreshSourceFiles();
            }
        }

        private bool _includeSubfolders;
        public bool IncludeSubfolders
        {
            get => _includeSubfolders;
            set
            {
                if (!Set(ref _includeSubfolders, value)) return;
                RefreshSourceFiles();
            }
        }

        public ObservableCollection<string> SourceFiles { get; } = new ObservableCollection<string>();

        public string SourceSummary => SourceFiles.Count == 0
            ? "Ningún archivo .dwg encontrado."
            : $"{SourceFiles.Count} archivo(s) .dwg encontrado(s).";

        private void RefreshSourceFiles()
        {
            SourceFiles.Clear();

            if (!string.IsNullOrWhiteSpace(SourceFolder) && Directory.Exists(SourceFolder))
            {
                try
                {
                    SearchOption option = IncludeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                    IEnumerable<string> files = Directory.EnumerateFiles(SourceFolder, "*.dwg", option)
                        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

                    foreach (string file in files) SourceFiles.Add(file);
                }
                catch (Exception ex)
                {
                    _log.Warn("No se pudo leer la carpeta de origen: " + ex.Message);
                }
            }

            Raise(nameof(SourceSummary), nameof(CanExport));
            Wizard?.Refresh();
            ExportCommand?.RaiseCanExecuteChanged();
        }

        private void BrowseSourceFolder()
        {
            string picked = _dialogs.PickFolder("Elige la carpeta con los dibujos (.dwg) a trazar", SourceFolder);
            if (picked != null) SourceFolder = picked;
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

        /// <summary>Each drawing gets its own subfolder inside this one; empty when the run only prints.</summary>
        public string DestinationFolder => Plot != null && Plot.WritesFiles ? OutputFolder : string.Empty;

        public string DestinationSummary
        {
            get
            {
                if (Plot != null && !Plot.WritesFiles)
                    return "Impresión directa: no se crean archivos ni carpetas.";

                return string.IsNullOrWhiteSpace(OutputFolder)
                    ? "Elige la carpeta donde se guardarán los archivos."
                    : $"Cada dibujo se guarda en su propia carpeta \"{Plot?.OutputFolderPrefix}_<dibujo>\" dentro de esta.";
            }
        }

        public string NamingHelpText => NamingTokens.HelpText;

        private string _namingPattern = "{DrawingTitle}-{LayoutName}";
        public string NamingPattern
        {
            get => _namingPattern;
            set => Set(ref _namingPattern, value);
        }

        private bool _openFolderWhenDone = true;
        public bool OpenFolderWhenDone
        {
            get => _openFolderWhenDone;
            set => Set(ref _openFolderWhenDone, value);
        }

        private void BrowseDestinationFolder()
        {
            string picked = _dialogs.PickFolder("Elige la carpeta de destino", OutputFolder);
            if (picked != null) OutputFolder = picked;
        }

        private void OnPlotSettingsChanged()
        {
            Wizard?.Refresh();
            Raise(nameof(DestinationFolder), nameof(DestinationSummary), nameof(CanExport));
            ExportCommand?.RaiseCanExecuteChanged();
            OpenFolderCommand?.RaiseCanExecuteChanged();
        }

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

        private string _statusText =
            "Los DWG deben estar cerrados en esta sesión. Cada archivo se abre brevemente para trazarlo y se cierra sin guardar; " +
            "al terminar vuelve tu dibujo activo.";
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

        public ObservableCollection<ExportResultItemViewModel> Results { get; } = new ObservableCollection<ExportResultItemViewModel>();

        public bool CanExport =>
            !IsExporting &&
            Wizard != null && Wizard.HasReachedLastStep &&
            SourceFiles.Count > 0 &&
            Plot != null && Plot.IsValid &&
            (!Plot.WritesFiles || !string.IsNullOrWhiteSpace(OutputFolder));

        // ---------------------------------------------------------------- commands

        public RelayCommand SourceFolderCommand { get; }

        public RelayCommand BrowseDestinationCommand { get; }

        public RelayCommand OpenFolderCommand { get; }

        public RelayCommand ExportCommand { get; }

        public RelayCommand CancelCommand { get; }

        private void StartExport()
        {
            if (!CanExport) return;

            List<string> files = SourceFiles.ToList();
            var engine = new PlotExportEngine();

            // Un archivo combinado por dibujo no aporta en este modo: cada dibujo ya va a su carpeta.
            PlotExportSettings settings = Plot.BuildSettings();
            settings.CombineIntoSingleFile = false;

            bool writesFiles = Plot.WritesFiles;
            string prefix = Plot.OutputFolderPrefix;
            _runWritesFiles = writesFiles;

            ExportRequest RequestFactory(Document document, DrawingSnapshot drawing)
            {
                string destination = string.Empty;
                if (writesFiles)
                {
                    string desired = Path.Combine(OutputFolder, prefix + "_" + drawing.Title);
                    destination = ExportPathHelper.AllocateUniqueDirectoryPath(desired);
                    ExportPathHelper.TryEnsureWritable(destination, out _);
                }

                return new ExportRequest(document.Database, destination, NamingPattern, settings, drawing, _log, document);
            }

            Results.Clear();
            ShowResults = false;
            ProgressValue = 0;
            ProgressMaximum = files.Count;
            IsExporting = true;
            StatusText = "Trazando...";

            var job = new FolderBatchExportJob(
                files,
                engine,
                RequestFactory,
                _log,
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
                _log.Error("No se pudo iniciar el trazado masivo de dibujos.", ex);
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
                return;
            }

            StatusText = result.WasCancelled
                ? $"Cancelado. {result.SucceededCount} dibujo(s) trazado(s) antes de cancelar."
                : $"Listo: {result.SucceededCount} dibujo(s) correcto(s), {result.FailedCount} con error.";

            if (OpenFolderWhenDone && _runWritesFiles && result.SucceededCount > 0)
                _dialogs.Reveal(OutputFolder);

            ShowCompletionDialog(result);
        }

        /// <summary>
        /// Tells the user the run is over. Counts are drawings, not layouts: one failed drawing may
        /// stand for several failed presentations, which the results grid spells out.
        /// </summary>
        private void ShowCompletionDialog(BatchResult result)
        {
            if (result.WasCancelled)
            {
                _dialogs.ShowWarning(DialogTitle,
                    $"Trazado cancelado.{Environment.NewLine}{Environment.NewLine}" +
                    $"Se alcanzaron a trazar {result.SucceededCount} dibujo(s).");
                return;
            }

            var message = new StringBuilder();
            message.AppendLine($"Se trazaron {result.SucceededCount} dibujo(s).");

            if (_runWritesFiles)
            {
                message.AppendLine();
                message.AppendLine("Carpeta:");
                message.Append(OutputFolder);
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

        private void ApplyPreferences(FolderBatchExportPreferences preferences)
        {
            SourceFolder = preferences.SourceFolder;
            IncludeSubfolders = preferences.IncludeSubfolders;
            OutputFolder = preferences.OutputFolder;
            NamingPattern = string.IsNullOrWhiteSpace(preferences.NamingPattern) ? NamingPattern : preferences.NamingPattern;
            OpenFolderWhenDone = preferences.OpenFolderWhenDone;
        }

        private void SavePreferences()
        {
            _settingsStore.Save(FolderBatchExportPreferences.StorageKey, new FolderBatchExportPreferences
            {
                SourceFolder = SourceFolder,
                IncludeSubfolders = IncludeSubfolders,
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
