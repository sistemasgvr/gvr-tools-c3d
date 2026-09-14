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
            InstallHqPlotterCommand = new RelayCommand(InstallHqPlotter);
            ExportCommand = new RelayCommand(StartExport, () => CanExport);
            CancelCommand = new RelayCommand(RequestCancel, () => IsExporting);

            LoadPlotStyleTables();
            LoadPlotDevices();

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
                    ReloadDeviceMediaNames();
                }
            }
        }

        public string PlotDeviceToolTip =>
            "Plotter PDF (.pc3) aplicado a todas las presentaciones de todos los DWG. " +
            "Los dibujos deben estar cerrados en esta sesión. Para sellos densos, usa un plotter HQ.";

        public IReadOnlyList<ChoiceItem<PdfPaperMode>> PaperModeChoices { get; } = ChoiceItem.List(
            ChoiceItem.Of(PdfPaperMode.ForceIsoFullBleed, "Forzar ISO full bleed"),
            ChoiceItem.Of(PdfPaperMode.UseLayout, "Usar tamaño del layout"),
            ChoiceItem.Of(PdfPaperMode.SelectFromDevice, "Elegir del dispositivo…"));

        private PdfPaperMode _paperMode = PdfPaperMode.ForceIsoFullBleed;
        public PdfPaperMode PaperMode
        {
            get => _paperMode;
            set
            {
                if (Set(ref _paperMode, value))
                {
                    Raise(nameof(ShowIsoFullBleedSize));
                    Raise(nameof(ShowDeviceMediaList));
                }
            }
        }

        public bool ShowIsoFullBleedSize => PaperMode == PdfPaperMode.ForceIsoFullBleed;

        public bool ShowDeviceMediaList => PaperMode == PdfPaperMode.SelectFromDevice;

        public string PaperModeToolTip =>
            "Por defecto se fuerza ISO full bleed A4. También puedes respetar cada layout o elegir del .pc3.";

        public IReadOnlyList<ChoiceItem<IsoFullBleedSize>> IsoSizeChoices { get; } = ChoiceItem.List(
            ChoiceItem.Of(IsoFullBleedSize.A0, "A0"),
            ChoiceItem.Of(IsoFullBleedSize.A1, "A1"),
            ChoiceItem.Of(IsoFullBleedSize.A2, "A2"),
            ChoiceItem.Of(IsoFullBleedSize.A3, "A3"),
            ChoiceItem.Of(IsoFullBleedSize.A4, "A4"));

        private IsoFullBleedSize _selectedIsoSize = IsoFullBleedSize.A4;
        public IsoFullBleedSize SelectedIsoSize
        {
            get => _selectedIsoSize;
            set => Set(ref _selectedIsoSize, value);
        }

        public string IsoSizeToolTip =>
            "Tamaño ISO full bleed. La orientación Vertical/Horizontal elige entre las variantes del plotter.";

        public ObservableCollection<string> DeviceMediaNames { get; } = new ObservableCollection<string>();

        private string _selectedDeviceMediaName = string.Empty;
        public string SelectedDeviceMediaName
        {
            get => _selectedDeviceMediaName;
            set => Set(ref _selectedDeviceMediaName, value ?? string.Empty);
        }

        public string DeviceMediaToolTip =>
            "Lista completa de papeles del plotter seleccionado (igual que el cuadro Imprimir de AutoCAD).";

        public IReadOnlyList<ChoiceItem<PdfPlotArea>> PlotAreaChoices { get; } = ChoiceItem.List(
            ChoiceItem.Of(PdfPlotArea.Extents, "Extents"),
            ChoiceItem.Of(PdfPlotArea.Window, "Window"),
            ChoiceItem.Of(PdfPlotArea.Display, "Display"),
            ChoiceItem.Of(PdfPlotArea.Layout, "Layout"));

        private PdfPlotArea _plotArea = PdfPlotArea.Extents;
        public PdfPlotArea PlotArea
        {
            get => _plotArea;
            set => Set(ref _plotArea, value);
        }

        public string PlotAreaToolTip =>
            "Extents (recomendado). Window requiere ventana ya definida en cada layout.";

        private bool _forcePlotPreset = true;
        public bool ForcePlotPreset
        {
            get => _forcePlotPreset;
            set
            {
                if (Set(ref _forcePlotPreset, value))
                {
                    Raise(nameof(ShowManualScaleOptions));
                    Raise(nameof(ShowCustomScaleFields));
                }
            }
        }

        public bool ShowManualScaleOptions => !ForcePlotPreset;

        public string ForcePlotPresetToolTip => "Preset GVR: escala 1:1, centrado, sin ajustar a la página.";

        private bool _fitToPaper;
        public bool FitToPaper
        {
            get => _fitToPaper;
            set
            {
                if (Set(ref _fitToPaper, value))
                    Raise(nameof(ShowCustomScaleFields));
            }
        }

        private bool _centerPlot = true;
        public bool CenterPlot
        {
            get => _centerPlot;
            set => Set(ref _centerPlot, value);
        }

        private bool _useCustomScale;
        public bool UseCustomScale
        {
            get => _useCustomScale;
            set
            {
                if (Set(ref _useCustomScale, value))
                    Raise(nameof(ShowCustomScaleFields));
            }
        }

        public bool ShowCustomScaleFields => !ForcePlotPreset && !FitToPaper && UseCustomScale;

        private double _customScaleNumerator = 1.0;
        public double CustomScaleNumerator
        {
            get => _customScaleNumerator;
            set => Set(ref _customScaleNumerator, value);
        }

        private double _customScaleDenominator = 1.0;
        public double CustomScaleDenominator
        {
            get => _customScaleDenominator;
            set => Set(ref _customScaleDenominator, value);
        }

        private bool _scaleLineweights;
        public bool ScaleLineweights
        {
            get => _scaleLineweights;
            set => Set(ref _scaleLineweights, value);
        }

        public IReadOnlyList<ChoiceItem<PdfPlotOrientation>> OrientationChoices { get; } = ChoiceItem.List(
            ChoiceItem.Of(PdfPlotOrientation.Landscape, "Horizontal"),
            ChoiceItem.Of(PdfPlotOrientation.Portrait, "Vertical"));

        private PdfPlotOrientation _plotOrientation = PdfPlotOrientation.Landscape;
        public PdfPlotOrientation PlotOrientation
        {
            get => _plotOrientation;
            set => Set(ref _plotOrientation, value);
        }

        public string OrientationToolTip =>
            "Orientación del dibujo en el papel (Horizontal = 0°, Vertical = 90°).";

        public ObservableCollection<string> PlotStyleTables { get; } = new ObservableCollection<string>();

        private string _selectedPlotStyleTable = PlotStyleTableRepository.KeepLayoutTableLabel;
        public string SelectedPlotStyleTable
        {
            get => _selectedPlotStyleTable;
            set => Set(ref _selectedPlotStyleTable, value ?? PlotStyleTableRepository.KeepLayoutTableLabel);
        }

        public string PlotStyleToolTip =>
            "Tabla de estilos (.ctb/.stb). Cada empresa usa la suya; impórtala si falta en este PC.";

        public string MissingPlotDeviceWarning =>
            PlotDevices.Count == 0
                ? "No hay dispositivos PDF (.pc3) instalados."
                : string.IsNullOrWhiteSpace(PlotDeviceName)
                    ? "Selecciona un dispositivo de trazado PDF."
                    : !PlotDevices.Contains(PlotDeviceName)
                        ? $"El dispositivo \"{PlotDeviceName}\" ya no está instalado."
                        : string.Empty;

        public bool HasMissingPlotDevice => !string.IsNullOrEmpty(MissingPlotDeviceWarning);

        private bool _plotTransparency = true;
        public bool PlotTransparency
        {
            get => _plotTransparency;
            set => Set(ref _plotTransparency, value);
        }

        private bool _plotObjectLineweights = true;
        public bool PlotObjectLineweights
        {
            get => _plotObjectLineweights;
            set => Set(ref _plotObjectLineweights, value);
        }

        private bool _plotWithPlotStyles = true;
        public bool PlotWithPlotStyles
        {
            get => _plotWithPlotStyles;
            set => Set(ref _plotWithPlotStyles, value);
        }

        private bool _plotPaperspaceLast = true;
        public bool PlotPaperspaceLast
        {
            get => _plotPaperspaceLast;
            set => Set(ref _plotPaperspaceLast, value);
        }

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

            ReloadDeviceMediaNames();
        }

        private void ReloadDeviceMediaNames()
        {
            string previous = SelectedDeviceMediaName;
            DeviceMediaNames.Clear();

            if (string.IsNullOrWhiteSpace(PlotDeviceName))
            {
                SelectedDeviceMediaName = string.Empty;
                return;
            }

            foreach (string media in PlotDeviceRepository.GetCanonicalMediaNames(PlotDeviceName))
                DeviceMediaNames.Add(media);

            if (!string.IsNullOrWhiteSpace(previous) && DeviceMediaNames.Contains(previous))
                SelectedDeviceMediaName = previous;
            else if (DeviceMediaNames.Count > 0)
                SelectedDeviceMediaName = DeviceMediaNames[0];
            else
                SelectedDeviceMediaName = string.Empty;
        }

        private PdfExportSettings BuildPdfSettings() => new PdfExportSettings
        {
            ForcePlotDevice = true,
            PlotDeviceName = PlotDeviceName,
            PaperMode = PaperMode,
            IsoFullBleedSize = SelectedIsoSize,
            SelectedCanonicalMediaName = SelectedDeviceMediaName,
            PlotArea = PlotArea,
            ForcePlotPreset = ForcePlotPreset,
            FitToPaper = FitToPaper,
            CenterPlot = CenterPlot,
            UseCustomScale = UseCustomScale,
            CustomScaleNumerator = CustomScaleNumerator,
            CustomScaleDenominator = CustomScaleDenominator,
            ScaleLineweights = ScaleLineweights,
            PlotOrientation = PlotOrientation,
            PlotTransparency = PlotTransparency,
            PlotObjectLineweights = PlotObjectLineweights,
            PlotWithPlotStyles = PlotWithPlotStyles,
            PlotPaperspaceLast = PlotPaperspaceLast,
            CombineIntoSinglePdf = false,
            PlotStyleTableOverride = ResolvePlotStyleOverride()
        };

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
            "Los DWG deben estar cerrados en esta sesión. Cada archivo se abre brevemente para exportar y se cierra sin guardar; " +
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
            SourceFiles.Count > 0 &&
            !string.IsNullOrWhiteSpace(OutputFolder) &&
            !HasMissingPlotDevice;

        // ---------------------------------------------------------------- commands

        public RelayCommand SourceFolderCommand { get; }

        public RelayCommand BrowseDestinationCommand { get; }

        public RelayCommand OpenFolderCommand { get; }

        public RelayCommand ImportPlotStyleCommand { get; }

        public RelayCommand InstallHqPlotterCommand { get; }

        public RelayCommand ExportCommand { get; }

        public RelayCommand CancelCommand { get; }

        private void InstallHqPlotter()
        {
            try
            {
                string installed = PlotDeviceRepository.InstallBundledHqPlotter(overwriteExisting: true);
                LoadPlotDevices();

                foreach (string device in PlotDevices)
                {
                    if (string.Equals(device, installed, StringComparison.OrdinalIgnoreCase))
                    {
                        PlotDeviceName = device;
                        break;
                    }
                }

                _dialogs.ShowInfo(DialogTitle,
                    $"Se instaló \"{installed}\" en la carpeta de plotters y quedó seleccionado.");
            }
            catch (Exception ex)
            {
                _log.Error("No se pudo instalar el plotter HQ.", ex);
                _dialogs.ShowError(DialogTitle, ex.Message);
            }
        }

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

                return new ExportRequest(document.Database, destination, NamingPattern, BuildPdfSettings(), drawing, _log, document);
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
            PaperMode = Enum.IsDefined(typeof(PdfPaperMode), preferences.PdfPaperMode)
                ? (PdfPaperMode)preferences.PdfPaperMode
                : PdfPaperMode.ForceIsoFullBleed;
            SelectedIsoSize = Enum.IsDefined(typeof(IsoFullBleedSize), preferences.PdfIsoFullBleedSize)
                ? (IsoFullBleedSize)preferences.PdfIsoFullBleedSize
                : IsoFullBleedSize.A4;
            PlotArea = Enum.IsDefined(typeof(PdfPlotArea), preferences.PdfPlotArea)
                ? (PdfPlotArea)preferences.PdfPlotArea
                : PdfPlotArea.Extents;
            ForcePlotPreset = preferences.PdfForcePlotPreset;
            FitToPaper = preferences.PdfFitToPaper;
            CenterPlot = preferences.PdfCenterPlot;
            UseCustomScale = preferences.PdfUseCustomScale;
            CustomScaleNumerator = preferences.PdfCustomScaleNumerator > 0
                ? preferences.PdfCustomScaleNumerator
                : 1.0;
            CustomScaleDenominator = preferences.PdfCustomScaleDenominator > 0
                ? preferences.PdfCustomScaleDenominator
                : 1.0;
            ScaleLineweights = preferences.PdfScaleLineweights;
            PlotOrientation = Enum.IsDefined(typeof(PdfPlotOrientation), preferences.PdfPlotOrientation)
                ? (PdfPlotOrientation)preferences.PdfPlotOrientation
                : PdfPlotOrientation.Landscape;
            PlotTransparency = preferences.PdfPlotTransparency;
            PlotObjectLineweights = preferences.PdfPlotObjectLineweights;
            PlotWithPlotStyles = preferences.PdfPlotWithPlotStyles;
            PlotPaperspaceLast = preferences.PdfPlotPaperspaceLast;

            string preferredDevice = preferences.PdfPlotDeviceName;
            if (!string.IsNullOrWhiteSpace(preferredDevice) && PlotDevices.Contains(preferredDevice))
                PlotDeviceName = preferredDevice;
            else
                PlotDeviceName = PlotDeviceRepository.ResolveDefaultDevice();

            if (!string.IsNullOrWhiteSpace(preferences.PdfSelectedMediaName) &&
                DeviceMediaNames.Contains(preferences.PdfSelectedMediaName))
            {
                SelectedDeviceMediaName = preferences.PdfSelectedMediaName;
            }

            SelectedPlotStyleTable = !string.IsNullOrEmpty(preferences.PdfPlotStyleTable) &&
                                     PlotStyleTables.Contains(preferences.PdfPlotStyleTable)
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
                PdfPlotDeviceName = PlotDeviceName,
                PdfPaperMode = (int)PaperMode,
                PdfIsoFullBleedSize = (int)SelectedIsoSize,
                PdfSelectedMediaName = SelectedDeviceMediaName,
                PdfPlotArea = (int)PlotArea,
                PdfForcePlotPreset = ForcePlotPreset,
                PdfFitToPaper = FitToPaper,
                PdfCenterPlot = CenterPlot,
                PdfUseCustomScale = UseCustomScale,
                PdfCustomScaleNumerator = CustomScaleNumerator,
                PdfCustomScaleDenominator = CustomScaleDenominator,
                PdfScaleLineweights = ScaleLineweights,
                PdfPlotOrientation = (int)PlotOrientation,
                PdfPlotTransparency = PlotTransparency,
                PdfPlotObjectLineweights = PlotObjectLineweights,
                PdfPlotWithPlotStyles = PlotWithPlotStyles,
                PdfPlotPaperspaceLast = PlotPaperspaceLast,
                PdfPlotStyleTable = ResolvePlotStyleOverride()
            });
        }

        public void Dispose()
        {
        }
    }
}
