using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using GvrTools.Civil3D.Export;
using GvrTools.Civil3D.Export.Plotting;
using GvrTools.Civil3D.Model;
using GvrTools.Core.Diagnostics;
using GvrTools.Core.IO;
using GvrTools.UI.Mvvm;
using GvrTools.UI.Services;

namespace GvrTools.Tools.BatchExport.ViewModels
{
    /// <summary>
    /// The "Trazado" tab: a batch version of Civil 3D's Plot dialog (page setup, printer/plotter,
    /// paper, copies, plot area, scale, offset, plot style table, shaded viewports, plot options,
    /// orientation) plus the GVR extras (ISO full bleed by orientation, CTB import, HQ plotter,
    /// common plot window, GVR preset).
    ///
    /// Shared by the active-drawing and the folder exporters: each host builds one, reads
    /// <see cref="BuildSettings"/> to run, and listens to <see cref="SettingsChanged"/> to refresh
    /// its own Export button and destination preview.
    /// </summary>
    public sealed class PlotConfigurationViewModel : ObservableObject
    {
        private readonly IUserDialogs _dialogs;
        private readonly ILog _log;
        private readonly string _dialogTitle;
        private readonly Func<string> _browseFolder;

        /// <summary>True while a combo's items are being rebuilt, when WPF pushes transient nulls back.</summary>
        private bool _rebuilding;

        private PlotDeviceDetails _deviceDetails;
        private PlotDeviceMedia _deviceMedia = PlotDeviceMedia.Empty;

        /// <param name="scales">The drawing's scale list (or the default metric list).</param>
        /// <param name="drawingPageSetups">Named page setups of the drawing the window was opened on.</param>
        /// <param name="browseFolder">Initial folder for the CTB / DWT pickers.</param>
        public PlotConfigurationViewModel(
            IUserDialogs dialogs,
            ILog log,
            string dialogTitle,
            IReadOnlyList<PlotScale> scales,
            IReadOnlyList<NamedPageSetup> drawingPageSetups,
            Func<string> browseFolder)
        {
            _dialogs = dialogs;
            _log = log ?? NullLog.Instance;
            _dialogTitle = dialogTitle;
            _browseFolder = browseFolder ?? (() => null);

            ImportPageSetupsCommand = new RelayCommand(ImportPageSetups);
            ApplyGvrPresetCommand = new RelayCommand(() => Apply(new PlotExportSettings()));
            RefreshDevicesCommand = new RelayCommand(RefreshDevices);
            InstallHqPlotterCommand = new RelayCommand(InstallHqPlotter);
            OpenPlottersFolderCommand = new RelayCommand(OpenPlottersFolder);
            ImportPlotStyleCommand = new RelayCommand(ImportPlotStyleTable);
            PickWindowCommand = new RelayCommand(PickWindow, () => CanPickWindow);

            ScaleChoices.Add(ScaleChoice.Custom);
            foreach (PlotScale scale in PlotScaleList.Normalize(scales))
                ScaleChoices.Add(new ScaleChoice(scale));

            PageSetupChoices.Add(PageSetupChoice.Manual);
            PageSetupChoices.Add(PageSetupChoice.LayoutOwn);
            foreach (NamedPageSetup setup in drawingPageSetups ?? Array.Empty<NamedPageSetup>())
                PageSetupChoices.Add(PageSetupChoice.Named(setup));

            LoadPlotStyleTables();
            LoadDevices();
            Apply(new PlotExportSettings());
        }

        /// <summary>Raised after any option changes, so the host can refresh what depends on it.</summary>
        public event EventHandler SettingsChanged;

        // ---------------------------------------------------------------- page setup

        public ObservableCollection<PageSetupChoice> PageSetupChoices { get; } = new ObservableCollection<PageSetupChoice>();

        private PageSetupChoice _selectedPageSetup = PageSetupChoice.Manual;
        public PageSetupChoice SelectedPageSetup
        {
            get => _selectedPageSetup;
            set
            {
                if (value == null || _rebuilding) return;
                if (!Set(ref _selectedPageSetup, value)) return;

                switch (value.Kind)
                {
                    case PageSetupChoiceKind.LayoutOwn:
                        Source = PlotConfigSource.LayoutOwn;
                        break;
                    case PageSetupChoiceKind.Named:
                        LoadNamedPageSetup(value.Setup);
                        break;
                    default:
                        Source = PlotConfigSource.Manual;
                        break;
                }
            }
        }

        private PlotConfigSource _source = PlotConfigSource.Manual;
        public PlotConfigSource Source
        {
            get => _source;
            private set
            {
                if (!Set(ref _source, value)) return;
                Raise(nameof(IsManual), nameof(IsLayoutOwn), nameof(CanSetCopies), nameof(CanPlotToFileEdit), nameof(PlotToFile));
                OnSettingsChanged();
            }
        }

        /// <summary>Most of the panel only applies when the options are set here (not per layout).</summary>
        public bool IsManual => Source == PlotConfigSource.Manual;

        public bool IsLayoutOwn => Source == PlotConfigSource.LayoutOwn;

        public string PageSetupToolTip =>
            "Igual que en el cuadro Trazar: elige una configuración de página con nombre para rellenar todo el panel, " +
            "o \"Propia de cada presentación\" para trazar cada layout tal como está configurado.";

        private void LoadNamedPageSetup(NamedPageSetup setup)
        {
            if (setup == null) return;

            PlotExportSettings values = setup.Settings;
            // El combinar/abrir carpeta pertenece a la salida, no a la configuración de página.
            values.CombineIntoSingleFile = CombineIntoSingleFile;
            values.Copies = Copies;
            values.PlotToFile = PlotToFile;

            Apply(values, keepPageSetupSelection: true);

            if (!string.IsNullOrWhiteSpace(values.PlotDeviceName) && !PlotDeviceRepository.IsInstalled(values.PlotDeviceName))
            {
                _dialogs.ShowWarning(_dialogTitle,
                    $"La configuración \"{setup.Name}\" usa el dispositivo \"{values.PlotDeviceName}\", " +
                    "que no está instalado en este equipo. Se dejó el dispositivo por defecto.");
            }
        }

        private void ImportPageSetups()
        {
            string picked = _dialogs.PickFile(
                "Importar configuraciones de página",
                "Plantillas y dibujos (*.dwt;*.dwg)|*.dwt;*.dwg|Todos los archivos (*.*)|*.*",
                _browseFolder());

            if (picked == null) return;

            IReadOnlyList<NamedPageSetup> setups;
            try
            {
                setups = PageSetupRepository.ReadFromFile(picked);
            }
            catch (PlotDeviceSetupException ex)
            {
                _log.Error("No se pudieron importar configuraciones de página.", ex);
                _dialogs.ShowError(_dialogTitle, ex.Message);
                return;
            }

            if (setups.Count == 0)
            {
                _dialogs.ShowInfo(_dialogTitle,
                    $"\"{System.IO.Path.GetFileName(picked)}\" no tiene configuraciones de página de presentación.");
                return;
            }

            PageSetupChoice first = null;
            foreach (NamedPageSetup setup in setups)
            {
                PageSetupChoice choice = PageSetupChoice.Named(setup);
                PageSetupChoices.Add(choice);
                if (first == null) first = choice;
            }

            SelectedPageSetup = first;
            _dialogs.ShowInfo(_dialogTitle,
                $"Se agregaron {setups.Count} configuración(es) de página de \"{System.IO.Path.GetFileName(picked)}\" " +
                $"y se aplicó \"{first.Setup.Name}\". Elige otra en el combo si la necesitas.");
        }

        // ---------------------------------------------------------------- printer / plotter

        public ObservableCollection<PlotDeviceItem> Devices { get; } = new ObservableCollection<PlotDeviceItem>();

        private PlotDeviceItem _selectedDevice;
        public PlotDeviceItem SelectedDevice
        {
            get => _selectedDevice;
            set
            {
                if (value == null || _rebuilding) return;
                if (!Set(ref _selectedDevice, value)) return;

                // Elegir otro dispositivo deja atrás el aviso del que faltaba.
                _missingDeviceNote = string.Empty;
                LoadSelectedDeviceDetails();
            }
        }

        public string PlotDeviceName => SelectedDevice?.Name ?? string.Empty;

        public string DeviceDriver => _deviceDetails?.DriverName ?? string.Empty;

        public string DeviceLocation => _deviceDetails?.Location ?? string.Empty;

        public string DeviceDescription => _deviceDetails?.Description ?? string.Empty;

        public string PlotDeviceToolTip =>
            "Todos los dispositivos del cuadro Trazar: impresoras de Windows y archivos .pc3 (PDF, DWF, PNG...). " +
            "Para sellos y textos densos instala el plotter HQ (DPI alto).";

        /// <summary>A remembered / page-setup device that is no longer installed.</summary>
        private string _missingDeviceNote = string.Empty;

        /// <summary>The selected device could not be loaded by AutoCAD.</summary>
        private string _loadFailureNote = string.Empty;

        public string DeviceWarning =>
            string.Join(" ", new[] { _missingDeviceNote, _loadFailureNote }.Where(n => !string.IsNullOrEmpty(n)));

        public bool HasDeviceWarning => !string.IsNullOrEmpty(DeviceWarning);

        private bool _plotToFile;
        /// <summary>
        /// "Trazar en archivo": forced on/off by devices that cannot choose. With per-layout
        /// configuration it means "for the printers that allow it".
        /// </summary>
        public bool PlotToFile
        {
            get
            {
                if (_deviceDetails == null || IsLayoutOwn) return _plotToFile;
                switch (_deviceDetails.PlotToFile)
                {
                    case PlotToFileMode.Always: return true;
                    case PlotToFileMode.Never: return false;
                    default: return _plotToFile;
                }
            }
            set
            {
                if (!CanPlotToFileEdit) return;
                if (_plotToFile == value) return;
                _plotToFile = value;
                Raise(nameof(PlotToFile), nameof(CanSetCopies));
                OnSettingsChanged();
            }
        }

        /// <summary>Editable only for Windows printers ("plot to file" optional).</summary>
        public bool CanPlotToFileEdit =>
            IsLayoutOwn || (_deviceDetails != null && _deviceDetails.PlotToFile == PlotToFileMode.Optional);

        private int _copies = 1;
        public int Copies
        {
            get => _copies;
            set
            {
                if (Set(ref _copies, value)) OnSettingsChanged();
            }
        }

        /// <summary>Copies only make sense when sheets go to a printer.</summary>
        public bool CanSetCopies => IsLayoutOwn || OutputKind == PlotOutputKind.Printer;

        private void LoadDevices()
        {
            _rebuilding = true;
            try
            {
                Devices.Clear();
                foreach (PlotDeviceInfo device in PlotDeviceRepository.GetAvailableDevices())
                    Devices.Add(new PlotDeviceItem(device));
            }
            finally
            {
                _rebuilding = false;
            }
        }

        private void SelectDevice(string deviceName)
        {
            PlotDeviceItem match = FindDevice(deviceName);
            string note = string.Empty;

            if (Devices.Count == 0)
            {
                note = "No hay dispositivos de trazado instalados.";
            }
            else if (match == null && !string.IsNullOrWhiteSpace(deviceName))
            {
                note = $"El dispositivo \"{deviceName}\" no está instalado en este equipo; se usa otro.";
                match = FindDevice(PlotDeviceRepository.ResolveDefaultDevice());
            }

            if (match == null && Devices.Count > 0) match = Devices[0];

            if (ReferenceEquals(match, _selectedDevice))
                LoadSelectedDeviceDetails();
            else if (match != null)
                SelectedDevice = match;

            // Después de seleccionar: el setter limpia el aviso pensando en un cambio del usuario.
            _missingDeviceNote = note;
            Raise(nameof(DeviceWarning), nameof(HasDeviceWarning));
        }

        private PlotDeviceItem FindDevice(string deviceName) =>
            string.IsNullOrWhiteSpace(deviceName)
                ? null
                : Devices.FirstOrDefault(d => string.Equals(d.Name, deviceName.Trim(), StringComparison.OrdinalIgnoreCase));

        private void LoadSelectedDeviceDetails()
        {
            string name = PlotDeviceName;
            _deviceDetails = PlotDeviceRepository.GetDeviceDetails(name);
            _deviceMedia = PlotDeviceRepository.GetMedia(name);

            _loadFailureNote = _deviceDetails == null && !string.IsNullOrEmpty(name)
                ? $"AutoCAD no pudo cargar \"{name}\". Revisa el dispositivo en el cuadro Trazar."
                : string.Empty;

            RebuildPaperChoices();

            Raise(nameof(DeviceWarning), nameof(HasDeviceWarning),
                nameof(PlotDeviceName), nameof(DeviceDriver), nameof(DeviceLocation), nameof(DeviceDescription),
                nameof(PlotToFile), nameof(CanPlotToFileEdit), nameof(CanSetCopies), nameof(IsRasterDevice),
                nameof(PaperUnitsLabel), nameof(CanEditOffset), nameof(ShadeDpiHint));
            OnSettingsChanged();
        }

        private void RefreshDevices()
        {
            string current = PlotDeviceName;
            PlotDeviceRepository.Refresh();
            LoadDevices();
            _selectedDevice = null;
            SelectDevice(current);
        }

        private void InstallHqPlotter()
        {
            try
            {
                string installed = PlotDeviceRepository.InstallBundledHqPlotter(overwriteExisting: true);
                PlotDeviceRepository.Refresh();
                LoadDevices();
                _selectedDevice = null;
                SelectDevice(installed);

                _dialogs.ShowInfo(_dialogTitle, $"Se instaló \"{installed}\" en la carpeta de plotters y quedó seleccionado.");
            }
            catch (Exception ex)
            {
                _log.Error("No se pudo instalar el plotter HQ.", ex);
                _dialogs.ShowError(_dialogTitle, ex.Message);
            }
        }

        /// <summary>The Plotters folder (what PLOTTERMANAGER opens): .pc3 files are edited from there.</summary>
        private void OpenPlottersFolder()
        {
            string folder = PlotDeviceRepository.GetPlotterConfigFolder();
            if (string.IsNullOrWhiteSpace(folder))
            {
                _dialogs.ShowWarning(_dialogTitle,
                    "No se encontró la carpeta de trazadores. Ábrela desde Civil 3D con el comando TRAZADORES (PLOTTERMANAGER).");
                return;
            }

            _dialogs.Reveal(folder);
        }

        // ---------------------------------------------------------------- paper

        public ObservableCollection<PaperChoice> PaperChoices { get; } = new ObservableCollection<PaperChoice>();

        private PaperChoice _selectedPaper;
        public PaperChoice SelectedPaper
        {
            get => _selectedPaper;
            set
            {
                if (value == null || _rebuilding) return;
                if (!Set(ref _selectedPaper, value)) return;

                _paperMode = value.Mode;
                if (value.Mode == PlotPaperMode.ForceIsoFullBleed) _isoSize = value.IsoSize;
                if (value.Mode == PlotPaperMode.SelectFromDevice) _canonicalMediaName = value.CanonicalName;
                OnSettingsChanged();
            }
        }

        private PlotPaperMode _paperMode = PlotPaperMode.ForceIsoFullBleed;
        private IsoFullBleedSize _isoSize = IsoFullBleedSize.A4;
        private string _canonicalMediaName = string.Empty;

        public string PaperToolTip =>
            "Los tamaños del dispositivo, con los mismos nombres que el cuadro Trazar. " +
            "Las opciones GVR fuerzan ISO full bleed eligiendo la variante según la orientación, o respetan el papel de cada presentación.";

        private void RebuildPaperChoices()
        {
            _rebuilding = true;
            try
            {
                PaperChoices.Clear();
                PaperChoices.Add(PaperChoice.UseLayout());

                IList<string> canonical = _deviceMedia.CanonicalNames;
                foreach (IsoFullBleedSize size in (IsoFullBleedSize[])Enum.GetValues(typeof(IsoFullBleedSize)))
                {
                    if (IsoFullBleedMediaPicker.Pick(size.ToString(), canonical) != null)
                        PaperChoices.Add(PaperChoice.Iso(size));
                }

                foreach (PlotMediaInfo media in _deviceMedia.Items)
                    PaperChoices.Add(PaperChoice.Device(media));
            }
            finally
            {
                _rebuilding = false;
            }

            SelectPaper(_paperMode, _isoSize, _canonicalMediaName);
        }

        /// <summary>
        /// Selects the paper matching the stored choice; when the new device does not offer it,
        /// falls back to the device's default size, as the Plot dialog does on a device change.
        /// </summary>
        private void SelectPaper(PlotPaperMode mode, IsoFullBleedSize isoSize, string canonicalName)
        {
            PaperChoice match = PaperChoices.FirstOrDefault(p => p.Matches(mode, isoSize, canonicalName))
                ?? PaperChoices.FirstOrDefault(p => p.Matches(PlotPaperMode.SelectFromDevice, isoSize, _deviceMedia.DefaultCanonicalName))
                ?? PaperChoices.FirstOrDefault(p => p.Mode == PlotPaperMode.SelectFromDevice)
                ?? PaperChoices.FirstOrDefault();

            _selectedPaper = null;
            if (match != null) SelectedPaper = match;
            else Raise(nameof(SelectedPaper));
        }

        // ---------------------------------------------------------------- plot area

        public IReadOnlyList<ChoiceItem<PlotAreaMode>> PlotAreaChoices { get; } = ChoiceItem.List(
            ChoiceItem.Of(PlotAreaMode.Layout, "Presentación"),
            ChoiceItem.Of(PlotAreaMode.Extents, "Extensión"),
            ChoiceItem.Of(PlotAreaMode.Display, "Pantalla"),
            ChoiceItem.Of(PlotAreaMode.Window, "Ventana"));

        private PlotAreaMode _plotArea = PlotAreaMode.Extents;
        public PlotAreaMode PlotArea
        {
            get => _plotArea;
            set
            {
                if (!Set(ref _plotArea, value)) return;
                Raise(nameof(IsWindowArea), nameof(CanCenter), nameof(CanEditOffset), nameof(CanPickWindow));
                PickWindowCommand.RaiseCanExecuteChanged();
                OnSettingsChanged();
            }
        }

        public bool IsWindowArea => PlotArea == PlotAreaMode.Window;

        public string PlotAreaToolTip =>
            "Extensión (recomendado): todo lo dibujado. Presentación: la hoja completa desde su origen. " +
            "Ventana: la guardada en cada presentación o una ventana común designada una vez.";

        private bool _useCommonWindow;
        public bool UseCommonWindow
        {
            get => _useCommonWindow;
            set
            {
                if (!Set(ref _useCommonWindow, value)) return;
                Raise(nameof(UseLayoutWindow), nameof(CanPickWindow));
                PickWindowCommand.RaiseCanExecuteChanged();
                OnSettingsChanged();
            }
        }

        public bool UseLayoutWindow
        {
            get => !UseCommonWindow;
            set => UseCommonWindow = !value;
        }

        private double _windowMinX;
        public double WindowMinX { get => _windowMinX; set { if (Set(ref _windowMinX, value)) OnSettingsChanged(); } }

        private double _windowMinY;
        public double WindowMinY { get => _windowMinY; set { if (Set(ref _windowMinY, value)) OnSettingsChanged(); } }

        private double _windowMaxX;
        public double WindowMaxX { get => _windowMaxX; set { if (Set(ref _windowMaxX, value)) OnSettingsChanged(); } }

        private double _windowMaxY;
        public double WindowMaxY { get => _windowMaxY; set { if (Set(ref _windowMaxY, value)) OnSettingsChanged(); } }

        /// <summary>
        /// Host hook for "Designar &lt;": asks the user for two corners in paper space. Null hides the
        /// button (the folder exporter has no drawing to pick on).
        /// </summary>
        public Func<Extents2d?> PickWindowHandler
        {
            get => _pickWindowHandler;
            set
            {
                _pickWindowHandler = value;
                Raise(nameof(PickWindowAvailable), nameof(CanPickWindow));
                PickWindowCommand.RaiseCanExecuteChanged();
            }
        }
        private Func<Extents2d?> _pickWindowHandler;

        public bool PickWindowAvailable => PickWindowHandler != null;

        public bool CanPickWindow => PickWindowHandler != null && IsWindowArea && UseCommonWindow;

        private void PickWindow()
        {
            Extents2d? picked = PickWindowHandler?.Invoke();
            if (picked == null) return;

            Extents2d window = picked.Value;
            WindowMinX = Math.Round(window.MinPoint.X, 4);
            WindowMinY = Math.Round(window.MinPoint.Y, 4);
            WindowMaxX = Math.Round(window.MaxPoint.X, 4);
            WindowMaxY = Math.Round(window.MaxPoint.Y, 4);
        }

        // ---------------------------------------------------------------- scale

        private bool _fitToPaper;
        /// <summary>"Escala hasta ajustar".</summary>
        public bool FitToPaper
        {
            get => _fitToPaper;
            set
            {
                if (!Set(ref _fitToPaper, value)) return;
                Raise(nameof(CanChooseScale), nameof(CanEditScaleValues));
                OnSettingsChanged();
            }
        }

        public bool CanChooseScale => !FitToPaper;

        public ObservableCollection<ScaleChoice> ScaleChoices { get; } = new ObservableCollection<ScaleChoice>();

        private ScaleChoice _selectedScale = ScaleChoice.Custom;
        public ScaleChoice SelectedScale
        {
            get => _selectedScale;
            set
            {
                if (value == null || _rebuilding) return;
                if (!Set(ref _selectedScale, value)) return;

                if (!value.IsCustom)
                {
                    _scalePaperUnits = value.Scale.PaperUnits;
                    _scaleDrawingUnits = value.Scale.DrawingUnits;
                    Raise(nameof(ScalePaperUnits), nameof(ScaleDrawingUnits));
                }

                Raise(nameof(CanEditScaleValues));
                OnSettingsChanged();
            }
        }

        public bool CanEditScaleValues => !FitToPaper && SelectedScale != null && SelectedScale.IsCustom;

        private double _scalePaperUnits = 1.0;
        public double ScalePaperUnits
        {
            get => _scalePaperUnits;
            set
            {
                if (Set(ref _scalePaperUnits, value)) OnSettingsChanged();
            }
        }

        private double _scaleDrawingUnits = 1.0;
        public double ScaleDrawingUnits
        {
            get => _scaleDrawingUnits;
            set
            {
                if (Set(ref _scaleDrawingUnits, value)) OnSettingsChanged();
            }
        }

        public IReadOnlyList<ChoiceItem<PlotPaperUnits>> PaperUnitChoices { get; } = ChoiceItem.List(
            ChoiceItem.Of(PlotPaperUnits.Millimeters, "mm"),
            ChoiceItem.Of(PlotPaperUnits.Inches, "pulgadas"));

        private PlotPaperUnits _paperUnits = PlotPaperUnits.Millimeters;
        public PlotPaperUnits PaperUnits
        {
            get => _paperUnits;
            set
            {
                if (value == PlotPaperUnits.Pixels) return;
                if (!Set(ref _paperUnits, value)) return;
                Raise(nameof(PaperUnitsLabel));
                OnSettingsChanged();
            }
        }

        /// <summary>PublishToWeb PNG/JPG and other raster devices measure paper in pixels.</summary>
        public bool IsRasterDevice =>
            _deviceDetails != null &&
            PlotOutput.UsesPixels(PlotOutput.Classify(_deviceDetails.DefaultFileExtension, plotsToFile: true));

        public string PaperUnitsLabel => IsRasterDevice ? "píxeles" : PaperUnits == PlotPaperUnits.Inches ? "pulgadas" : "mm";

        private bool _scaleLineweights;
        public bool ScaleLineweights
        {
            get => _scaleLineweights;
            set
            {
                if (Set(ref _scaleLineweights, value)) OnSettingsChanged();
            }
        }

        private void SelectScaleFor(double paperUnits, double drawingUnits)
        {
            _scalePaperUnits = paperUnits > 0 ? paperUnits : 1.0;
            _scaleDrawingUnits = drawingUnits > 0 ? drawingUnits : 1.0;

            ScaleChoice match = ScaleChoices.FirstOrDefault(c => !c.IsCustom && c.Scale.HasSameRatio(_scalePaperUnits, _scaleDrawingUnits));
            _selectedScale = match ?? ScaleChoice.Custom;

            Raise(nameof(SelectedScale), nameof(ScalePaperUnits), nameof(ScaleDrawingUnits), nameof(CanEditScaleValues));
        }

        // ---------------------------------------------------------------- offset

        private bool _centerPlot = true;
        public bool CenterPlot
        {
            get => _centerPlot;
            set
            {
                if (!Set(ref _centerPlot, value)) return;
                Raise(nameof(CanEditOffset));
                OnSettingsChanged();
            }
        }

        /// <summary>AutoCAD cannot center a "Presentación" plot (the Plot dialog greys the checkbox out).</summary>
        public bool CanCenter => PlotArea != PlotAreaMode.Layout;

        public bool CanEditOffset => (!CenterPlot || !CanCenter) && !IsRasterDevice;

        private double _offsetX;
        public double OffsetX
        {
            get => _offsetX;
            set
            {
                if (Set(ref _offsetX, value)) OnSettingsChanged();
            }
        }

        private double _offsetY;
        public double OffsetY
        {
            get => _offsetY;
            set
            {
                if (Set(ref _offsetY, value)) OnSettingsChanged();
            }
        }

        // ---------------------------------------------------------------- orientation

        private PlotDrawingOrientation _orientation = PlotDrawingOrientation.FromLayout;

        /// <summary>"Según cada presentación": each layout keeps its own orientation.</summary>
        public bool IsFromLayoutOrientation
        {
            get => _orientation == PlotDrawingOrientation.FromLayout;
            set
            {
                if (value) SetOrientation(PlotDrawingOrientation.FromLayout);
            }
        }

        /// <summary>"Girado 180°" only applies to a fixed orientation; per layout it comes from the layout.</summary>
        public bool CanPlotUpsideDown => !IsFromLayoutOrientation;

        public bool IsLandscape
        {
            get => _orientation == PlotDrawingOrientation.Landscape;
            set
            {
                if (value) SetOrientation(PlotDrawingOrientation.Landscape);
            }
        }

        public bool IsPortrait
        {
            get => _orientation == PlotDrawingOrientation.Portrait;
            set
            {
                if (value) SetOrientation(PlotDrawingOrientation.Portrait);
            }
        }

        private void SetOrientation(PlotDrawingOrientation orientation)
        {
            if (_orientation == orientation) return;
            _orientation = orientation;
            Raise(nameof(IsLandscape), nameof(IsPortrait), nameof(IsFromLayoutOrientation), nameof(CanPlotUpsideDown));
            OnSettingsChanged();
        }

        private bool _plotUpsideDown;
        public bool PlotUpsideDown
        {
            get => _plotUpsideDown;
            set
            {
                if (Set(ref _plotUpsideDown, value)) OnSettingsChanged();
            }
        }

        public string OrientationToolTip =>
            "Según cada presentación (recomendado): respeta la orientación de cada layout, útil si mezclas hojas verticales y apaisadas. " +
            "Horizontal: el borde largo del papel queda arriba. Con ISO full bleed también elige la variante del papel.";

        // ---------------------------------------------------------------- plot style table

        public ObservableCollection<string> PlotStyleTables { get; } = new ObservableCollection<string>();

        private string _selectedPlotStyleTable = PlotStyleTableRepository.KeepLayoutTableLabel;
        public string SelectedPlotStyleTable
        {
            get => _selectedPlotStyleTable;
            set
            {
                if (_rebuilding) return;
                if (Set(ref _selectedPlotStyleTable, value ?? PlotStyleTableRepository.KeepLayoutTableLabel))
                    OnSettingsChanged();
            }
        }

        public string PlotStyleToolTip =>
            "Tabla de estilos (.ctb/.stb) con plumas de color y grosor. " +
            "Cada empresa usa la suya; impórtala si no está instalada en este PC.";

        /// <summary>
        /// Host hook: tables the selected layouts ask for that are not installed (empty when the
        /// host cannot know, as in the folder exporter).
        /// </summary>
        public Func<IReadOnlyList<string>> MissingPlotStyleTablesProvider { get; set; }

        public string MissingPlotStyleWarning
        {
            get
            {
                IReadOnlyList<string> missing = MissingPlotStyleTablesProvider?.Invoke() ?? Array.Empty<string>();
                if (missing.Count == 0 || !string.IsNullOrEmpty(ResolvePlotStyleOverride())) return string.Empty;

                return $"Falta la tabla de estilos {string.Join(", ", missing)}. " +
                       "Usa \"Examinar...\" para instalarla desde el proyecto, o elige otra de la lista.";
            }
        }

        public bool HasMissingPlotStyle => !string.IsNullOrEmpty(MissingPlotStyleWarning);

        /// <summary>Host hook: the layouts that will be plotted (null when unknown, as in the folder exporter).</summary>
        public Func<IReadOnlyList<LayoutSnapshot>> SelectedLayoutsProvider { get; set; }

        /// <summary>
        /// Warns when the forced paper is smaller than the sheets the selected layouts were drawn
        /// for at the chosen scale (an A1 layout on ISO A4 at 1:1 plots only its middle).
        /// </summary>
        public string ClipWarning
        {
            get
            {
                if (!IsManual || FitToPaper || ScalePaperUnits <= 0 || ScaleDrawingUnits <= 0) return string.Empty;
                if (!TryGetTargetPaperSize(out double width, out double height, out string paperLabel)) return string.Empty;

                IReadOnlyList<LayoutSnapshot> layouts = SelectedLayoutsProvider?.Invoke();
                if (layouts == null || layouts.Count == 0) return string.Empty;

                double ratio = ScalePaperUnits / ScaleDrawingUnits;
                var clipped = new List<LayoutSnapshot>();
                foreach (LayoutSnapshot layout in layouts)
                {
                    if (PlotMediaNames.TryGetSizeInMillimeters(layout.CanonicalMediaName, out double w, out double h) &&
                        PlotPaperFit.WouldClip(w, h, width, height, ratio))
                    {
                        clipped.Add(layout);
                    }
                }

                if (clipped.Count == 0) return string.Empty;

                string scale = SelectedScale != null && !SelectedScale.IsCustom
                    ? SelectedScale.Label
                    : PlotScale.FormatName(ScalePaperUnits, ScaleDrawingUnits);

                return $"{clipped.Count} presentación(es) están dibujadas para un papel mayor " +
                       $"({PlotMediaNames.Humanize(clipped[0].CanonicalMediaName)}) que {paperLabel}. " +
                       $"A escala {scale} saldrán cortadas: marca \"Escala hasta ajustar\" o elige \"Tamaño de cada presentación\".";
            }
        }

        public bool HasClipWarning => !string.IsNullOrEmpty(ClipWarning);

        private bool TryGetTargetPaperSize(out double width, out double height, out string label)
        {
            width = 0;
            height = 0;
            label = SelectedPaper?.Label ?? string.Empty;

            switch (_paperMode)
            {
                case PlotPaperMode.ForceIsoFullBleed:
                    label = "ISO full bleed " + _isoSize;
                    return PlotPaperFit.TryGetIsoSize(_isoSize.ToString(), out width, out height);
                case PlotPaperMode.SelectFromDevice:
                    return PlotMediaNames.TryGetSizeInMillimeters(_canonicalMediaName, out width, out height);
                default:
                    // Cada presentación con su propio papel: no se fuerza nada más pequeño.
                    return false;
            }
        }

        /// <summary>Call when the host's layout selection changes.</summary>
        public void RefreshLayoutWarnings() =>
            Raise(nameof(MissingPlotStyleWarning), nameof(HasMissingPlotStyle), nameof(ClipWarning), nameof(HasClipWarning),
                nameof(CanCombine), nameof(CombineBlocker), nameof(OutputSummary));

        private void LoadPlotStyleTables()
        {
            string previous = _selectedPlotStyleTable;

            _rebuilding = true;
            try
            {
                PlotStyleTableRepository.Refresh();
                PlotStyleTables.Clear();
                PlotStyleTables.Add(PlotStyleTableRepository.KeepLayoutTableLabel);
                foreach (string table in PlotStyleTableRepository.GetAvailableTables())
                    PlotStyleTables.Add(table);
            }
            finally
            {
                _rebuilding = false;
            }

            SelectPlotStyleTable(previous);
        }

        private void SelectPlotStyleTable(string tableName)
        {
            string match = PlotStyleTables.FirstOrDefault(t => string.Equals(t, tableName, StringComparison.OrdinalIgnoreCase));
            _selectedPlotStyleTable = match ?? PlotStyleTableRepository.KeepLayoutTableLabel;
            Raise(nameof(SelectedPlotStyleTable));
            RefreshLayoutWarnings();
        }

        /// <summary>The chosen table, or empty when the user kept each layout's own.</summary>
        private string ResolvePlotStyleOverride() =>
            string.Equals(SelectedPlotStyleTable, PlotStyleTableRepository.KeepLayoutTableLabel, StringComparison.Ordinal)
                ? string.Empty
                : SelectedPlotStyleTable;

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
                _browseFolder());

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
                    bool replace = _dialogs.Confirm(_dialogTitle,
                        $"{exists.Message}{Environment.NewLine}{Environment.NewLine}" +
                        "¿Reemplazarla con la del proyecto?");

                    if (!replace)
                    {
                        // Se conserva la ya instalada: basta con seleccionarla.
                        SelectPlotStyleTable(exists.TableName);
                        OnSettingsChanged();
                        return;
                    }

                    installed = PlotStyleTableRepository.Import(picked, overwriteExisting: true, installAs: installAs);
                }

                LoadPlotStyleTables();
                SelectPlotStyleTable(installed);
                OnSettingsChanged();

                _log.Info($"Tabla de estilos '{installed}' importada desde '{picked}'.");
                _dialogs.ShowInfo(_dialogTitle,
                    $"Se instaló la tabla de estilos \"{installed}\" y quedó seleccionada para este trazado.");
            }
            catch (PlotStyleImportException ex)
            {
                _log.Error("No se pudo importar la tabla de estilos.", ex);
                _dialogs.ShowError(_dialogTitle, ex.Message);
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
            IReadOnlyList<string> missing = MissingPlotStyleTablesProvider?.Invoke() ?? Array.Empty<string>();
            if (missing.Count != 1) return null;

            string wanted = missing[0];
            string pickedName = System.IO.Path.GetFileName(pickedFile);

            if (string.Equals(pickedName, wanted, StringComparison.OrdinalIgnoreCase))
                return null;

            bool rename = _dialogs.Confirm(_dialogTitle,
                $"Las presentaciones piden \"{wanted}\", pero seleccionaste \"{pickedName}\"." +
                $"{Environment.NewLine}{Environment.NewLine}" +
                $"¿Instalarla como \"{wanted}\" para que las presentaciones la encuentren?" +
                $"{Environment.NewLine}{Environment.NewLine}" +
                $"Si eliges Cancelar se instalará como \"{pickedName}\" y tendrás que seleccionarla a mano.");

            return rename ? wanted : null;
        }

        // ---------------------------------------------------------------- plot options

        private bool _plotWithPlotStyles = true;
        public bool PlotWithPlotStyles
        {
            get => _plotWithPlotStyles;
            set
            {
                if (!Set(ref _plotWithPlotStyles, value)) return;
                Raise(nameof(PlotObjectLineweights), nameof(CanEditLineweights));
                OnSettingsChanged();
            }
        }

        private bool _plotObjectLineweights = true;
        /// <summary>As in the Plot dialog, plotting with plot styles implies plotting lineweights.</summary>
        public bool PlotObjectLineweights
        {
            get => _plotObjectLineweights || PlotWithPlotStyles;
            set
            {
                if (Set(ref _plotObjectLineweights, value)) OnSettingsChanged();
            }
        }

        public bool CanEditLineweights => !PlotWithPlotStyles;

        private bool _plotTransparency = true;
        public bool PlotTransparency
        {
            get => _plotTransparency;
            set
            {
                if (Set(ref _plotTransparency, value)) OnSettingsChanged();
            }
        }

        private bool _plotPaperspaceLast = true;
        public bool PlotPaperspaceLast
        {
            get => _plotPaperspaceLast;
            set
            {
                if (Set(ref _plotPaperspaceLast, value)) OnSettingsChanged();
            }
        }

        private bool _hidePaperspaceObjects;
        public bool HidePaperspaceObjects
        {
            get => _hidePaperspaceObjects;
            set
            {
                if (Set(ref _hidePaperspaceObjects, value)) OnSettingsChanged();
            }
        }

        // ---------------------------------------------------------------- shaded viewports

        public IReadOnlyList<ChoiceItem<PlotShadeQuality>> ShadeQualityChoices { get; } = ChoiceItem.List(
            ChoiceItem.Of(PlotShadeQuality.Draft, "Borrador"),
            ChoiceItem.Of(PlotShadeQuality.Preview, "Vista preliminar"),
            ChoiceItem.Of(PlotShadeQuality.Normal, "Normal"),
            ChoiceItem.Of(PlotShadeQuality.Presentation, "Presentación"),
            ChoiceItem.Of(PlotShadeQuality.Maximum, "Máximo"),
            ChoiceItem.Of(PlotShadeQuality.Custom, "Personalizado"));

        private PlotShadeQuality _shadeQuality = PlotShadeQuality.Normal;
        public PlotShadeQuality ShadeQuality
        {
            get => _shadeQuality;
            set
            {
                if (!Set(ref _shadeQuality, value)) return;
                Raise(nameof(IsCustomShadeDpi));
                OnSettingsChanged();
            }
        }

        public bool IsCustomShadeDpi => ShadeQuality == PlotShadeQuality.Custom;

        private int _shadeCustomDpi = 300;
        public int ShadeCustomDpi
        {
            get => _shadeCustomDpi;
            set
            {
                if (Set(ref _shadeCustomDpi, value)) OnSettingsChanged();
            }
        }

        public string ShadeDpiHint => _deviceDetails != null && _deviceDetails.MaximumDpi > 0
            ? $"máx. {_deviceDetails.MaximumDpi}"
            : string.Empty;

        public string ShadeToolTip =>
            "Calidad de las ventanas gráficas sombreadas o modelizadas. En presentaciones, el modo de sombreado " +
            "lo define cada ventana gráfica (igual que en el cuadro Trazar), por eso aquí solo se elige la calidad.";

        // ---------------------------------------------------------------- output

        private bool _combineIntoSingleFile;
        public bool CombineIntoSingleFile
        {
            get => _combineIntoSingleFile;
            set
            {
                if (Set(ref _combineIntoSingleFile, value)) OnSettingsChanged();
            }
        }

        /// <summary>Null when the output depends on each layout (per-layout configuration).</summary>
        public PlotOutputKind? OutputKind =>
            IsManual && _deviceDetails != null ? _deviceDetails.ResolveOutput(PlotToFile) : (PlotOutputKind?)null;

        /// <summary>Whether the run writes files (and so needs a destination folder).</summary>
        public bool WritesFiles => OutputKind == null || PlotOutput.IsFileOutput(OutputKind.Value);

        /// <summary>
        /// One multi-sheet file is possible only for PDF / DWF, with a single device, and when every
        /// sheet gets the same paper: AutoCAD refuses to mix papers in one plot document
        /// (<c>PlotInfo.IsCompatibleDocument</c>).
        /// </summary>
        public bool CanCombine => string.IsNullOrEmpty(CombineBlocker);

        /// <summary>Why "combine" is unavailable, for the hint under the checkbox; empty when it is available.</summary>
        public string CombineBlocker
        {
            get
            {
                if (!OutputKind.HasValue)
                    return "Combinar solo está disponible con la configuración manual (un único dispositivo).";
                if (!PlotOutput.SupportsMultiSheet(OutputKind.Value))
                    return "Solo los dispositivos PDF y DWF admiten varias hojas en un mismo archivo.";
                if (!AllSheetsShareMedia())
                    return "Las presentaciones seleccionadas usan tamaños de papel distintos y AutoCAD no los mezcla " +
                           "en un mismo archivo. Fuerza un tamaño (por ejemplo ISO full bleed) para combinarlas.";
                return string.Empty;
            }
        }

        private bool AllSheetsShareMedia()
        {
            if (_paperMode != PlotPaperMode.UseLayout) return true;

            IReadOnlyList<LayoutSnapshot> layouts = SelectedLayoutsProvider?.Invoke();
            if (layouts == null) return false;

            return layouts
                .Select(l => l.CanonicalMediaName ?? string.Empty)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() <= 1;
        }

        /// <summary>Prefix of the per-run output folder ("PDF", "DWF", "PNG", "Trazado").</summary>
        public string OutputFolderPrefix => OutputKind.HasValue
            ? PlotOutput.FolderPrefix(OutputKind.Value, _deviceDetails.DefaultFileExtension)
            : "Trazado";

        public string OutputSummary
        {
            get
            {
                if (!OutputKind.HasValue)
                    return "Salida: según la configuración de cada presentación (archivo o impresora).";

                PlotOutputKind kind = OutputKind.Value;
                if (kind == PlotOutputKind.Printer)
                    return Copies > 1
                        ? $"Salida: impresión directa en \"{PlotDeviceName}\", {Copies} copias por presentación."
                        : $"Salida: impresión directa en \"{PlotDeviceName}\".";

                string what = PlotOutput.Describe(kind, _deviceDetails.DefaultFileExtension);
                return CombineIntoSingleFile && CanCombine
                    ? $"Salida: un solo archivo {what} con todas las presentaciones."
                    : $"Salida: un archivo {what} ({_deviceDetails.DefaultFileExtension}) por presentación.";
            }
        }

        /// <summary>First problem that prevents plotting, or null when everything is usable.</summary>
        public string ValidationError
        {
            get
            {
                if (Devices.Count == 0) return "No hay dispositivos de trazado instalados.";
                if (IsLayoutOwn) return null;

                if (SelectedDevice == null) return "Selecciona un dispositivo de trazado.";
                if (_deviceDetails == null) return $"AutoCAD no pudo cargar el dispositivo \"{PlotDeviceName}\".";
                if (SelectedPaper == null) return "Selecciona un tamaño de papel.";
                if (!FitToPaper && (ScalePaperUnits <= 0 || ScaleDrawingUnits <= 0))
                    return "La escala de trazado debe usar valores mayores que cero.";
                if (IsWindowArea && UseCommonWindow &&
                    (Math.Abs(WindowMaxX - WindowMinX) <= 1e-9 || Math.Abs(WindowMaxY - WindowMinY) <= 1e-9))
                    return "Designa la ventana común o escribe sus esquinas.";
                if (OutputKind == PlotOutputKind.Printer && (Copies < 1 || Copies > 999))
                    return "El número de copias debe estar entre 1 y 999.";
                if (IsCustomShadeDpi && ShadeCustomDpi < 100)
                    return "Los PPP de las ventanas sombreadas deben ser 100 o más.";

                return null;
            }
        }

        public bool IsValid => ValidationError == null;

        // ---------------------------------------------------------------- commands

        public RelayCommand ImportPageSetupsCommand { get; }

        public RelayCommand ApplyGvrPresetCommand { get; }

        public RelayCommand RefreshDevicesCommand { get; }

        public RelayCommand InstallHqPlotterCommand { get; }

        public RelayCommand OpenPlottersFolderCommand { get; }

        public RelayCommand ImportPlotStyleCommand { get; }

        public RelayCommand PickWindowCommand { get; }

        // ---------------------------------------------------------------- settings in / out

        public PlotExportSettings BuildSettings() => new PlotExportSettings
        {
            Source = Source,
            PlotDeviceName = PlotDeviceName,
            // La elección del usuario, no el valor forzado: el motor ya lo fuerza según el dispositivo,
            // y así una impresora no hereda "en archivo" de un PDF usado antes.
            PlotToFile = _plotToFile,
            Copies = Copies,
            PaperMode = _paperMode,
            IsoFullBleedSize = _isoSize,
            SelectedCanonicalMediaName = _canonicalMediaName,
            PlotArea = PlotArea,
            UseCommonWindow = UseCommonWindow,
            WindowMinX = WindowMinX,
            WindowMinY = WindowMinY,
            WindowMaxX = WindowMaxX,
            WindowMaxY = WindowMaxY,
            FitToPaper = FitToPaper,
            ScalePaperUnits = ScalePaperUnits,
            ScaleDrawingUnits = ScaleDrawingUnits,
            PaperUnits = PaperUnits,
            ScaleLineweights = ScaleLineweights,
            CenterPlot = CenterPlot,
            OffsetX = OffsetX,
            OffsetY = OffsetY,
            Orientation = _orientation,
            PlotUpsideDown = PlotUpsideDown,
            PlotStyleTableOverride = ResolvePlotStyleOverride(),
            PlotObjectLineweights = PlotObjectLineweights,
            PlotTransparency = PlotTransparency,
            PlotWithPlotStyles = PlotWithPlotStyles,
            PlotPaperspaceLast = PlotPaperspaceLast,
            HidePaperspaceObjects = HidePaperspaceObjects,
            ShadeQuality = ShadeQuality,
            ShadeCustomDpi = ShadeCustomDpi,
            CombineIntoSingleFile = CombineIntoSingleFile && CanCombine
        };

        /// <summary>
        /// Fills the panel from <paramref name="settings"/> (remembered preferences, the GVR preset
        /// or a named page setup).
        /// </summary>
        public void Apply(PlotExportSettings settings, bool keepPageSetupSelection = false)
        {
            if (settings == null) return;

            _plotToFile = settings.PlotToFile;
            _copies = settings.Copies > 0 ? settings.Copies : 1;
            _paperMode = settings.PaperMode;
            _isoSize = settings.IsoFullBleedSize;
            _canonicalMediaName = settings.SelectedCanonicalMediaName ?? string.Empty;
            _plotArea = settings.PlotArea;
            _useCommonWindow = settings.UseCommonWindow;
            _windowMinX = settings.WindowMinX;
            _windowMinY = settings.WindowMinY;
            _windowMaxX = settings.WindowMaxX;
            _windowMaxY = settings.WindowMaxY;
            _fitToPaper = settings.FitToPaper;
            _paperUnits = settings.PaperUnits == PlotPaperUnits.Inches ? PlotPaperUnits.Inches : PlotPaperUnits.Millimeters;
            _scaleLineweights = settings.ScaleLineweights;
            _centerPlot = settings.CenterPlot;
            _offsetX = settings.OffsetX;
            _offsetY = settings.OffsetY;
            _orientation = settings.Orientation;
            _plotUpsideDown = settings.PlotUpsideDown;
            _plotObjectLineweights = settings.PlotObjectLineweights;
            _plotTransparency = settings.PlotTransparency;
            _plotWithPlotStyles = settings.PlotWithPlotStyles;
            _plotPaperspaceLast = settings.PlotPaperspaceLast;
            _hidePaperspaceObjects = settings.HidePaperspaceObjects;
            _shadeQuality = settings.ShadeQuality;
            _shadeCustomDpi = settings.ShadeCustomDpi > 0 ? settings.ShadeCustomDpi : 300;
            _combineIntoSingleFile = settings.CombineIntoSingleFile;

            SelectScaleFor(settings.ScalePaperUnits, settings.ScaleDrawingUnits);
            SelectPlotStyleTable(string.IsNullOrWhiteSpace(settings.PlotStyleTableOverride)
                ? PlotStyleTableRepository.KeepLayoutTableLabel
                : settings.PlotStyleTableOverride);

            if (!keepPageSetupSelection)
            {
                _selectedPageSetup = settings.Source == PlotConfigSource.LayoutOwn
                    ? PageSetupChoice.LayoutOwn
                    : PageSetupChoice.Manual;
                Raise(nameof(SelectedPageSetup));
            }

            _source = settings.Source;

            // El dispositivo va después de los valores: al cambiarlo se reconstruye la lista de papel
            // y se vuelve a elegir el papel guardado arriba.
            _selectedDevice = null;
            SelectDevice(settings.PlotDeviceName);

            Raise(string.Empty);
            OnSettingsChanged();
        }

        private void OnSettingsChanged()
        {
            Raise(nameof(ValidationError), nameof(IsValid), nameof(OutputKind), nameof(WritesFiles),
                nameof(CanCombine), nameof(OutputFolderPrefix), nameof(OutputSummary), nameof(CanSetCopies),
                nameof(ClipWarning), nameof(HasClipWarning), nameof(CombineBlocker));
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
