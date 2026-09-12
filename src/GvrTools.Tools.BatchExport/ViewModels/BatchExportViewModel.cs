using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
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
            LoadPlotStyleTables();
            LoadPlotDevices();

            // Los comandos deben existir ANTES de aplicar las preferencias: al asignar OutputFolder
            // su setter llama a ExportCommand.RaiseCanExecuteChanged(), y si ApplyPreferences corre
            // primero ExportCommand aún es null → NullReferenceException al abrir la ventana.
            SelectAllCommand = new RelayCommand(() => SetSelection(true));
            SelectNoneCommand = new RelayCommand(() => SetSelection(false));
            BrowseFolderCommand = new RelayCommand(BrowseFolder);
            OpenFolderCommand = new RelayCommand(() => _dialogs.Reveal(DestinationFolder));
            ImportPlotStyleCommand = new RelayCommand(ImportPlotStyleTable);
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

                item.PropertyChanged += (s, e) =>
                    Raise(nameof(SelectionSummary), nameof(CanExport), nameof(MissingPlotStyleWarning), nameof(HasMissingPlotStyle));
                Layouts.Add(item);
            }
        }

        public int SelectedCount => Layouts.Count(item => item.IsSelected);

        public string SelectionSummary => $"{SelectedCount} de {Layouts.Count} presentaciones seleccionadas";

        private void SetSelection(bool selected)
        {
            foreach (LayoutItemViewModel item in Layouts) item.IsSelected = selected;
            Raise(nameof(SelectionSummary), nameof(CanExport), nameof(MissingPlotStyleWarning), nameof(HasMissingPlotStyle));
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

        /// <summary>Installed PDF .pc3 devices (e.g. DWG To PDF.pc3 and custom HQ copies).</summary>
        public ObservableCollection<string> PlotDevices { get; } = new ObservableCollection<string>();

        private string _plotDeviceName = PlotDeviceRepository.DefaultPdfDeviceName;
        public string PlotDeviceName
        {
            get => _plotDeviceName;
            set
            {
                if (Set(ref _plotDeviceName, value ?? string.Empty))
                {
                    Raise(nameof(CanExport), nameof(MissingPlotDeviceWarning), nameof(HasMissingPlotDevice));
                    ExportCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        public string PlotDeviceHelpText =>
            "Elige el .pc3 instalado (Imprimir → Administrar estilos de trazado / Plotters). " +
            "Para CIP y sellos legibles crea una copia HQ de \"DWG To PDF.pc3\" (Propiedades personalizadas, DPI alto) y selecciónala aquí. " +
            "Se aplica a todas las presentaciones del lote.";

        public string PlotPresetHelpText =>
            "Preset GVR en cada hoja: Extents, escala 1:1, centrado, sin ajustar a la página. " +
            "El tamaño de papel lo define cada presentación. Extents exige que no haya dibujo en el espacio gris fuera del papel.";

        private bool _plotTransparency = true;
        public bool PlotTransparency
        {
            get => _plotTransparency;
            set => Set(ref _plotTransparency, value);
        }

        /// <summary>
        /// Installed plot style tables, plus a first entry meaning "leave each layout's own table".
        /// </summary>
        public ObservableCollection<string> PlotStyleTables { get; } = new ObservableCollection<string>();

        private string _selectedPlotStyleTable = PlotStyleTableRepository.KeepLayoutTableLabel;
        public string SelectedPlotStyleTable
        {
            get => _selectedPlotStyleTable;
            set => Set(ref _selectedPlotStyleTable, value ?? PlotStyleTableRepository.KeepLayoutTableLabel);
        }

        public string PlotStyleHelpText =>
            "La tabla CTB/STB controla colores y grosores al imprimir (a diferencia de Revit). " +
            "Sin la tabla correcta el PDF sale con colores crudos y sellos/CIP pueden quedar ilegibles.";

        /// <summary>Tables the selected layouts ask for that are not installed on this machine.</summary>
        private List<string> GetMissingPlotStyleTables()
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

        /// <summary>Empty unless some selected layout wants a table that is not installed here.</summary>
        public string MissingPlotStyleWarning
        {
            get
            {
                List<string> missing = GetMissingPlotStyleTables();
                if (missing.Count == 0) return string.Empty;

                return $"Falta la tabla de estilos {string.Join(", ", missing)}. " +
                       "Usa \"Examinar...\" para instalarla desde el proyecto, o elige otra de la lista.";
            }
        }

        public bool HasMissingPlotStyle => !string.IsNullOrEmpty(MissingPlotStyleWarning);

        public string MissingPlotDeviceWarning =>
            PlotDevices.Count == 0
                ? "No hay dispositivos PDF (.pc3) instalados. Instala o registra un plotter PDF en AutoCAD."
                : string.IsNullOrWhiteSpace(PlotDeviceName)
                    ? "Selecciona un dispositivo de trazado PDF."
                    : !PlotDevices.Contains(PlotDeviceName)
                        ? $"El dispositivo \"{PlotDeviceName}\" ya no está instalado."
                        : string.Empty;

        public bool HasMissingPlotDevice => !string.IsNullOrEmpty(MissingPlotDeviceWarning);

        private void LoadPlotStyleTables()
        {
            PlotStyleTableRepository.Refresh();

            PlotStyleTables.Clear();
            PlotStyleTables.Add(PlotStyleTableRepository.KeepLayoutTableLabel);

            foreach (string table in PlotStyleTableRepository.GetAvailableTables())
                PlotStyleTables.Add(table);
        }

        private void LoadPlotDevices()
        {
            PlotDeviceRepository.Refresh();

            PlotDevices.Clear();
            foreach (string device in PlotDeviceRepository.GetAvailablePdfDevices())
                PlotDevices.Add(device);
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
            !string.IsNullOrWhiteSpace(OutputFolder) &&
            !HasMissingPlotDevice;

        // ---------------------------------------------------------------- commands

        public RelayCommand SelectAllCommand { get; }

        public RelayCommand SelectNoneCommand { get; }

        public RelayCommand BrowseFolderCommand { get; }

        public RelayCommand OpenFolderCommand { get; }

        public RelayCommand ImportPlotStyleCommand { get; }

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
                ForcePlotDevice = true,
                ForcePlotPreset = true,
                PlotDeviceName = PlotDeviceName,
                PlotTransparency = PlotTransparency,
                CombineIntoSinglePdf = CombineIntoSinglePdf,
                PlotStyleTableOverride = ResolvePlotStyleOverride()
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

        /// <summary>
        /// Lets the user point at a .ctb/.stb that ships with the project instead of one already
        /// installed. The plot engine resolves style tables by NAME out of AutoCAD's own folders, so
        /// the file is copied there first and then selected.
        /// </summary>
        private void ImportPlotStyleTable()
        {
            string initial = _drawing.LocalFolder;

            string picked = _dialogs.PickFile(
                "Selecciona la tabla de estilos del proyecto",
                "Tablas de estilos (*.ctb;*.stb)|*.ctb;*.stb|Todos los archivos (*.*)|*.*",
                initial);

            if (picked == null) return;

            try
            {
                string installAs = ResolveInstallName(picked);

                string installed;
                try
                {
                    installed = PlotStyleTableRepository.Import(picked, overwriteExisting: false, installAs: installAs);
                }
                catch (PlotStyleAlreadyExistsException exists)
                {
                    bool replace = _dialogs.Confirm(DialogTitle,
                        $"{exists.Message}{Environment.NewLine}{Environment.NewLine}" +
                        "¿Reemplazarla con la del proyecto?");

                    if (!replace)
                    {
                        // Se conserva la ya instalada: basta con seleccionarla.
                        SelectInstalledTable(exists.TableName);
                        return;
                    }

                    installed = PlotStyleTableRepository.Import(picked, overwriteExisting: true, installAs: installAs);
                }

                LoadPlotStyleTables();
                SelectInstalledTable(installed);
                Raise(nameof(MissingPlotStyleWarning), nameof(HasMissingPlotStyle));

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

        /// <summary>
        /// Name the picked file should be installed under.
        ///
        /// The plot engine matches style tables by name, so a project that ships "Lombardi 3.ctb"
        /// does NOT satisfy layouts asking for "Lombardi.ctb" — same pens, wrong name, still
        /// missing. When the file name differs from the table the layouts want, this offers to
        /// install it under the expected name. Returns null to keep the file's own name.
        /// </summary>
        private string ResolveInstallName(string pickedFile)
        {
            List<string> missing = GetMissingPlotStyleTables();
            if (missing.Count != 1) return null;

            string wanted = missing[0];
            string pickedName = System.IO.Path.GetFileName(pickedFile);

            if (string.Equals(pickedName, wanted, StringComparison.OrdinalIgnoreCase))
                return null;

            bool rename = _dialogs.Confirm(DialogTitle,
                $"Las presentaciones piden \"{wanted}\", pero seleccionaste \"{pickedName}\"." +
                $"{Environment.NewLine}{Environment.NewLine}" +
                $"¿Instalarla como \"{wanted}\" para que las presentaciones la encuentren?" +
                $"{Environment.NewLine}{Environment.NewLine}" +
                $"Si eliges Cancelar se instalará como \"{pickedName}\" y tendrás que seleccionarla a mano.");

            return rename ? wanted : null;
        }

        private void SelectInstalledTable(string tableName)
        {
            foreach (string table in PlotStyleTables)
            {
                if (string.Equals(table, tableName, StringComparison.OrdinalIgnoreCase))
                {
                    SelectedPlotStyleTable = table;
                    Raise(nameof(MissingPlotStyleWarning), nameof(HasMissingPlotStyle));
                    return;
                }
            }
        }

        /// <summary>The chosen table, or empty when the user kept each layout's own.</summary>
        private string ResolvePlotStyleOverride() =>
            string.Equals(SelectedPlotStyleTable, PlotStyleTableRepository.KeepLayoutTableLabel, StringComparison.Ordinal)
                ? string.Empty
                : SelectedPlotStyleTable;

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

            string destination = result.DestinationFolder;

            if (OpenFolderWhenDone && result.SucceededCount > 0)
                _dialogs.Reveal(destination);

            ShowCompletionDialog(result, destination);

            _runDestinationFolder = null;
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
                    $"Exportación cancelada.{Environment.NewLine}{Environment.NewLine}" +
                    $"Se alcanzaron a exportar {result.SucceededCount} presentación(es).");
                return;
            }

            var message = new StringBuilder();
            message.AppendLine($"Se exportaron {result.SucceededCount} presentación(es) a PDF.");
            message.AppendLine();
            message.AppendLine("Carpeta:");
            message.Append(destinationFolder);

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
            NamingPattern = preferences.NamingPattern;
            OpenFolderWhenDone = preferences.OpenFolderWhenDone;
            CombineIntoSinglePdf = preferences.PdfCombineIntoSinglePdf;
            PlotTransparency = preferences.PdfPlotTransparency;

            string preferredDevice = preferences.PdfPlotDeviceName;
            if (!string.IsNullOrWhiteSpace(preferredDevice) && PlotDevices.Contains(preferredDevice))
                PlotDeviceName = preferredDevice;
            else
                PlotDeviceName = PlotDeviceRepository.ResolveDefaultDevice();

            SelectedPlotStyleTable = !string.IsNullOrEmpty(preferences.PdfPlotStyleTable) &&
                                     PlotStyleTables.Contains(preferences.PdfPlotStyleTable)
                ? preferences.PdfPlotStyleTable
                : PlotStyleTableRepository.KeepLayoutTableLabel;
        }

        private void SavePreferences()
        {
            _settingsStore.Save(BatchExportPreferences.StorageKey, new BatchExportPreferences
            {
                OutputFolder = OutputFolder,
                NamingPattern = NamingPattern,
                OpenFolderWhenDone = OpenFolderWhenDone,
                PdfPlotDeviceName = PlotDeviceName,
                PdfPlotTransparency = PlotTransparency,
                PdfCombineIntoSinglePdf = CombineIntoSinglePdf,
                PdfPlotStyleTable = ResolvePlotStyleOverride()
            });
        }

        public void Dispose()
        {
        }
    }
}
