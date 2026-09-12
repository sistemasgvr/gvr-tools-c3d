using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using GvrTools.Civil3D.Export;
using GvrTools.Civil3D.Export.Pdf;
using GvrTools.Civil3D.Infrastructure;
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
    /// Window logic for the multi-drawing exporter: picks a source folder of .dwg files instead of
    /// a list of layouts from the active drawing, and hands each file to
    /// <see cref="FolderBatchExportJob"/>, which opens/exports/closes it without the user ever
    /// seeing it as a normal open drawing.
    /// </summary>
    public sealed class FolderBatchExportViewModel : ObservableObject, IDisposable
    {
        private const string DialogTitle = "GVR Tools - Exportación masiva de dibujos";

        private readonly CivilJobScheduler _scheduler;
        private readonly IUserDialogs _dialogs;
        private readonly ISettingsStore _settingsStore;
        private readonly ILog _log;

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
            OpenFolderCommand = new RelayCommand(() => _dialogs.Reveal(DestinationFolder));
            ImportPlotStyleCommand = new RelayCommand(ImportPlotStyleTable);
            ExportCommand = new RelayCommand(StartExport, () => CanExport);
            CancelCommand = new RelayCommand(RequestCancel, () => IsExporting);

            LoadPlotStyleTables();

            FolderBatchExportPreferences preferences =
                _settingsStore.Load<FolderBatchExportPreferences>(FolderBatchExportPreferences.StorageKey);
            ApplyPreferences(preferences);

            RefreshSourceFiles();
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
                Raise(nameof(CanExport));
                ExportCommand?.RaiseCanExecuteChanged();
            }
        }

        public bool IncludeSubfolders
        {
            get => _includeSubfolders;
            set
            {
                if (!Set(ref _includeSubfolders, value)) return;
                RefreshSourceFiles();
            }
        }
        private bool _includeSubfolders;

        public ObservableCollection<string> SourceFiles { get; } = new ObservableCollection<string>();

        public string SourceSummary => SourceFiles.Count == 0
            ? "Ningún archivo .dwg encontrado."
            : $"{SourceFiles.Count} archivo(s) .dwg encontrado(s).";

        private void RefreshSourceFiles()
        {
            SourceFiles.Clear();

            if (string.IsNullOrWhiteSpace(SourceFolder) || !Directory.Exists(SourceFolder))
            {
                Raise(nameof(SourceSummary));
                return;
            }

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

            Raise(nameof(SourceSummary));
        }

        private void BrowseSourceFolder()
        {
            string picked = _dialogs.PickFolder("Elige la carpeta con los dibujos (.dwg) a exportar", SourceFolder);
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
                Raise(nameof(CanExport));
                ExportCommand?.RaiseCanExecuteChanged();
            }
        }

        public string DestinationFolder => OutputFolder;

        public string NamingHelpText => NamingTokens.HelpText;

        private string _namingPattern = "{DrawingTitle}-{LayoutName}";
        public string NamingPattern
        {
            get => _namingPattern;
            set => Set(ref _namingPattern, value);
        }

        private void BrowseDestinationFolder()
        {
            string picked = _dialogs.PickFolder("Elige la carpeta de destino", OutputFolder);
            if (picked != null) OutputFolder = picked;
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

        /// <summary>
        /// Installed plot style tables, plus a first entry meaning "leave each layout's own table".
        /// Across many drawings the tables they reference vary, so this is the one place a user can
        /// force a single, known-installed set of pen assignments for the whole run.
        /// </summary>
        public ObservableCollection<string> PlotStyleTables { get; } = new ObservableCollection<string>();

        private string _selectedPlotStyleTable = PlotStyleTableRepository.KeepLayoutTableLabel;
        public string SelectedPlotStyleTable
        {
            get => _selectedPlotStyleTable;
            set => Set(ref _selectedPlotStyleTable, value ?? PlotStyleTableRepository.KeepLayoutTableLabel);
        }

        private void LoadPlotStyleTables()
        {
            // Al abrir la ventana se relee, por si se instalaron tablas desde la última vez.
            PlotStyleTableRepository.Refresh();

            PlotStyleTables.Clear();
            PlotStyleTables.Add(PlotStyleTableRepository.KeepLayoutTableLabel);

            foreach (string table in PlotStyleTableRepository.GetAvailableTables())
                PlotStyleTables.Add(table);
        }

        /// <summary>
        /// Lets the user point at a .ctb/.stb that ships with the project instead of one already
        /// installed. The plot engine resolves style tables by NAME out of AutoCAD's own folders, so
        /// the file is copied there first and then selected.
        /// </summary>
        private void ImportPlotStyleTable()
        {
            string picked = _dialogs.PickFile(
                "Selecciona la tabla de estilos del proyecto",
                "Tablas de estilos (*.ctb;*.stb)|*.ctb;*.stb|Todos los archivos (*.*)|*.*",
                SourceFolder);

            if (picked == null) return;

            try
            {
                string installed;
                try
                {
                    installed = PlotStyleTableRepository.Import(picked, overwriteExisting: false);
                }
                catch (PlotStyleAlreadyExistsException exists)
                {
                    bool replace = _dialogs.Confirm(DialogTitle,
                        $"{exists.Message}{Environment.NewLine}{Environment.NewLine}" +
                        "¿Reemplazarla con la del proyecto?");

                    if (!replace)
                    {
                        SelectInstalledTable(exists.TableName);
                        return;
                    }

                    installed = PlotStyleTableRepository.Import(picked, overwriteExisting: true);
                }

                LoadPlotStyleTables();
                SelectInstalledTable(installed);

                _log.Info($"Tabla de estilos '{installed}' importada desde '{picked}'.");
                _dialogs.ShowInfo(DialogTitle,
                    $"Se instaló la tabla de estilos \"{installed}\" y quedó seleccionada para esta exportación.");
            }
            catch (PlotStyleImportException ex)
            {
                _log.Error("No se pudo importar la tabla de estilos.", ex);
                _dialogs.ShowError(DialogTitle, ex.Message);
            }
        }

        private void SelectInstalledTable(string tableName)
        {
            foreach (string table in PlotStyleTables)
            {
                if (string.Equals(table, tableName, StringComparison.OrdinalIgnoreCase))
                {
                    SelectedPlotStyleTable = table;
                    return;
                }
            }
        }

        /// <summary>The chosen table, or empty when the user kept each layout's own.</summary>
        private string ResolvePlotStyleOverride() =>
            string.Equals(SelectedPlotStyleTable, PlotStyleTableRepository.KeepLayoutTableLabel, StringComparison.Ordinal)
                ? string.Empty
                : SelectedPlotStyleTable;

        private bool _openFolderWhenDone = true;
        public bool OpenFolderWhenDone
        {
            get => _openFolderWhenDone;
            set => Set(ref _openFolderWhenDone, value);
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
            "Advertencia: cada dibujo se abre brevemente en esta sesión de Civil 3D mientras se exporta " +
            "(no es posible hacerlo sin abrirlo); la pestaña activa vuelve a la tuya al terminar.";
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
            SourceFiles.Count > 0 &&
            !string.IsNullOrWhiteSpace(OutputFolder);

        // ---------------------------------------------------------------- commands

        public RelayCommand SourceFolderCommand { get; }

        public RelayCommand BrowseDestinationCommand { get; }

        public RelayCommand OpenFolderCommand { get; }

        public RelayCommand ImportPlotStyleCommand { get; }

        public RelayCommand ExportCommand { get; }

        public RelayCommand CancelCommand { get; }

        private void StartExport()
        {
            if (!CanExport) return;

            List<string> files = SourceFiles.ToList();

            var engine = new PdfExportEngine();

            ExportRequest RequestFactory(Document document, DrawingSnapshot drawing)
            {
                string desired = Path.Combine(OutputFolder, "PDF_" + drawing.Title);
                string destination = ExportPathHelper.AllocateUniqueDirectoryPath(desired);
                ExportPathHelper.TryEnsureWritable(destination, out _);

                var settings = new PdfExportSettings
                {
                    UseLayoutPageSetup = UseLayoutPageSetup,
                    PlotDeviceName = PlotDeviceName,
                    FitToPaper = FitToPaper,
                    CombineIntoSinglePdf = false,
                    PlotStyleTableOverride = ResolvePlotStyleOverride()
                };

                return new ExportRequest(document.Database, destination, NamingPattern, settings, drawing, _log, document);
            }

            Results.Clear();
            ShowResults = false;
            ProgressValue = 0;
            ProgressMaximum = files.Count;
            IsExporting = true;
            StatusText = "Exportando...";

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
                _log.Error("No se pudo iniciar la exportación masiva de dibujos.", ex);
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
                ? $"Cancelado. {result.SucceededCount} dibujo(s) exportado(s) antes de cancelar."
                : $"Listo: {result.SucceededCount} dibujo(s) correcto(s), {result.FailedCount} con error.";

            if (OpenFolderWhenDone && result.SucceededCount > 0)
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
                    $"Exportación cancelada.{Environment.NewLine}{Environment.NewLine}" +
                    $"Se alcanzaron a exportar {result.SucceededCount} dibujo(s).");
                return;
            }

            var message = new StringBuilder();
            message.AppendLine($"Se exportaron {result.SucceededCount} dibujo(s) a PDF.");
            message.AppendLine();
            message.AppendLine("Carpeta:");
            message.Append(OutputFolder);

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
            UseLayoutPageSetup = preferences.PdfUseLayoutPageSetup;
            PlotDeviceName = preferences.PdfPlotDeviceName;
            FitToPaper = preferences.PdfFitToPaper;

            // Solo se restaura si la tabla sigue instalada; si no, vuelve a "usar la del layout".
            SelectedPlotStyleTable = PlotStyleTables.Contains(preferences.PdfPlotStyleTable)
                ? preferences.PdfPlotStyleTable
                : PlotStyleTableRepository.KeepLayoutTableLabel;
        }

        private void SavePreferences()
        {
            _settingsStore.Save(FolderBatchExportPreferences.StorageKey, new FolderBatchExportPreferences
            {
                SourceFolder = SourceFolder,
                IncludeSubfolders = IncludeSubfolders,
                OutputFolder = OutputFolder,
                NamingPattern = NamingPattern,
                OpenFolderWhenDone = OpenFolderWhenDone,
                PdfUseLayoutPageSetup = UseLayoutPageSetup,
                PdfPlotDeviceName = PlotDeviceName,
                PdfFitToPaper = FitToPaper,
                PdfPlotStyleTable = ResolvePlotStyleOverride()
            });
        }

        public void Dispose()
        {
        }
    }
}
