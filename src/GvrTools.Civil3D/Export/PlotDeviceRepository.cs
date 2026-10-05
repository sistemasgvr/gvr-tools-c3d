using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Reflection;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.PlottingServices;
using GvrTools.Civil3D.Infrastructure;
using GvrTools.Core.IO;

namespace GvrTools.Civil3D.Export
{
    /// <summary>Whether a plot device is a Windows printer or an AutoCAD .pc3 configuration.</summary>
    public enum PlotDeviceKind
    {
        SystemPrinter,
        Pc3
    }

    /// <summary>Whether a device writes a file, prints, or can do either ("Plot to file" checkbox).</summary>
    public enum PlotToFileMode
    {
        Never,
        Optional,
        Always
    }

    /// <summary>One entry of the device combo, in the order AutoCAD's Plot dialog lists them.</summary>
    public sealed class PlotDeviceInfo
    {
        public PlotDeviceInfo(string name, PlotDeviceKind kind)
        {
            Name = name;
            Kind = kind;
        }

        public string Name { get; }

        public PlotDeviceKind Kind { get; }

        public override string ToString() => Name;
    }

    /// <summary>What the Plot dialog shows under the device combo, plus how the device writes.</summary>
    public sealed class PlotDeviceDetails
    {
        public PlotDeviceDetails(
            string name,
            string driverName,
            string location,
            string description,
            PlotToFileMode plotToFile,
            string defaultFileExtension,
            int maximumDpi)
        {
            Name = name ?? string.Empty;
            DriverName = driverName ?? string.Empty;
            Location = location ?? string.Empty;
            Description = description ?? string.Empty;
            PlotToFile = plotToFile;
            DefaultFileExtension = PlotOutput.NormalizeExtension(defaultFileExtension);
            MaximumDpi = maximumDpi;
        }

        public string Name { get; }

        /// <summary>"Trazador" line: the driver, e.g. "DWG To PDF - PDF ePlot - by Autodesk".</summary>
        public string DriverName { get; }

        /// <summary>"Lugar" line: port or location ("Archivo", "USB001", a network share...).</summary>
        public string Location { get; }

        /// <summary>"Descripción" line: the comment stored in the .pc3 / printer.</summary>
        public string Description { get; }

        public PlotToFileMode PlotToFile { get; }

        /// <summary>Normalized, with the dot: ".pdf", ".dwf", ".png", ".plt".</summary>
        public string DefaultFileExtension { get; }

        /// <summary>Highest DPI the device supports (0 when unknown); caps the shaded-viewport DPI.</summary>
        public int MaximumDpi { get; }

        /// <summary>Whether a run with the given "plot to file" choice writes a file.</summary>
        public bool WritesFile(bool plotToFileRequested) =>
            PlotToFile == PlotToFileMode.Always ||
            (PlotToFile == PlotToFileMode.Optional && plotToFileRequested);

        public PlotOutputKind ResolveOutput(bool plotToFileRequested) =>
            PlotOutput.Classify(DefaultFileExtension, WritesFile(plotToFileRequested));
    }

    /// <summary>One paper size: the canonical name AutoCAD stores and the localized name it shows.</summary>
    public sealed class PlotMediaInfo
    {
        public PlotMediaInfo(string canonicalName, string localName)
        {
            CanonicalName = canonicalName ?? string.Empty;
            LocalName = string.IsNullOrWhiteSpace(localName) ? PlotMediaNames.Humanize(CanonicalName) : localName;
        }

        public string CanonicalName { get; }

        /// <summary>As the Plot dialog shows it, e.g. "ISO full bleed A1 (841.00 x 594.00 mm)".</summary>
        public string LocalName { get; }

        public override string ToString() => LocalName;
    }

    /// <summary>The paper sizes of one device, in the device's own order, plus its default size.</summary>
    public sealed class PlotDeviceMedia
    {
        public static readonly PlotDeviceMedia Empty = new PlotDeviceMedia(Array.Empty<PlotMediaInfo>(), string.Empty);

        public PlotDeviceMedia(IReadOnlyList<PlotMediaInfo> items, string defaultCanonicalName)
        {
            Items = items ?? Array.Empty<PlotMediaInfo>();
            DefaultCanonicalName = defaultCanonicalName ?? string.Empty;
        }

        public IReadOnlyList<PlotMediaInfo> Items { get; }

        /// <summary>Media AutoCAD picks when the device is first assigned (the device default).</summary>
        public string DefaultCanonicalName { get; }

        public IList<string> CanonicalNames
        {
            get
            {
                var names = new List<string>(Items.Count);
                foreach (PlotMediaInfo item in Items) names.Add(item.CanonicalName);
                return names;
            }
        }

        public PlotMediaInfo Find(string canonicalName)
        {
            foreach (PlotMediaInfo item in Items)
            {
                if (string.Equals(item.CanonicalName, canonicalName, StringComparison.OrdinalIgnoreCase))
                    return item;
            }

            return null;
        }
    }

    /// <summary>
    /// Everything the Plot dialog knows about plot devices: the full device list
    /// (<see cref="PlotSettingsValidator.GetPlotDeviceList"/> — Windows printers and .pc3 files),
    /// each device's driver/port/description and plot-to-file behaviour
    /// (<see cref="PlotConfigManager.SetCurrentConfig"/>), and its paper sizes with the localized
    /// names (<see cref="PlotSettingsValidator.GetLocaleMediaName(PlotSettings,int)"/>).
    ///
    /// Asking a Windows printer driver for its configuration can take a while (network printers
    /// especially), so every answer is cached until <see cref="Refresh"/>.
    /// Also installs a bundled high-quality PC3 into the plotters folder when present.
    /// </summary>
    public static class PlotDeviceRepository
    {
        public const string DefaultPdfDeviceName = "DWG To PDF.pc3";

        /// <summary>File name of the optional high-DPI plotter shipped next to the add-in.</summary>
        public const string BundledHqDeviceFileName = "DWG To PDF_HQ_.pc3";

        private static IReadOnlyList<PlotDeviceInfo> _cachedDevices;
        private static HashSet<string> _cachedLookup;
        private static readonly Dictionary<string, PlotDeviceDetails> DetailsCache =
            new Dictionary<string, PlotDeviceDetails>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, PlotDeviceMedia> MediaCache =
            new Dictionary<string, PlotDeviceMedia>(StringComparer.OrdinalIgnoreCase);
        private static readonly object CacheLock = new object();

        /// <summary>
        /// Every device of the Plot dialog except "None", in AutoCAD's order (Windows printers
        /// first, then .pc3 files). Empty when AutoCAD cannot be asked.
        /// </summary>
        public static IReadOnlyList<PlotDeviceInfo> GetAvailableDevices()
        {
            lock (CacheLock)
            {
                if (_cachedDevices != null) return _cachedDevices;

                var devices = new List<PlotDeviceInfo>();
                try
                {
                    Dictionary<string, PlotDeviceKind> kinds = ReadDeviceKinds();
                    StringCollection list = PlotSettingsValidator.Current.GetPlotDeviceList();

                    foreach (string name in list)
                    {
                        if (string.IsNullOrWhiteSpace(name) || IsNoneDevice(name)) continue;

                        PlotDeviceKind kind;
                        if (!kinds.TryGetValue(name, out kind))
                            kind = name.EndsWith(".pc3", StringComparison.OrdinalIgnoreCase)
                                ? PlotDeviceKind.Pc3
                                : PlotDeviceKind.SystemPrinter;

                        devices.Add(new PlotDeviceInfo(name, kind));
                    }
                }
                catch (Exception)
                {
                    return new List<PlotDeviceInfo>();
                }

                _cachedDevices = devices;
                _cachedLookup = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (PlotDeviceInfo device in devices) _cachedLookup.Add(device.Name);
                return _cachedDevices;
            }
        }

        /// <summary>Forgets every cached answer and asks AutoCAD to rescan printers and .pc3 files.</summary>
        public static void Refresh()
        {
            lock (CacheLock)
            {
                _cachedDevices = null;
                _cachedLookup = null;
                DetailsCache.Clear();
                MediaCache.Clear();
            }

            try
            {
                PlotConfigManager.RefreshList(RefreshCode.All);
            }
            catch (Exception)
            {
                // Sin host o sin permisos: la próxima consulta usará la lista que AutoCAD ya tenga.
            }
        }

        public static bool IsInstalled(string deviceName)
        {
            if (string.IsNullOrWhiteSpace(deviceName)) return false;
            GetAvailableDevices();
            lock (CacheLock)
            {
                return _cachedLookup != null && _cachedLookup.Contains(deviceName.Trim());
            }
        }

        /// <summary>The "None" entry of the Plot dialog: a layout with it cannot be plotted.</summary>
        public static bool IsNoneDevice(string deviceName)
        {
            string trimmed = (deviceName ?? string.Empty).Trim();
            return trimmed.Length == 0 ||
                   string.Equals(trimmed, "None", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(trimmed, "Ninguno", StringComparison.OrdinalIgnoreCase);
        }

        public static string ResolveDefaultDevice()
        {
            IReadOnlyList<PlotDeviceInfo> devices = GetAvailableDevices();
            if (devices.Count == 0) return string.Empty;

            foreach (PlotDeviceInfo device in devices)
            {
                if (string.Equals(device.Name, DefaultPdfDeviceName, StringComparison.OrdinalIgnoreCase))
                    return device.Name;
            }

            return devices[0].Name;
        }

        /// <summary>
        /// Driver, port, description and plot-to-file behaviour of <paramref name="deviceName"/>,
        /// or null when AutoCAD rejects the device (missing .pc3, broken driver).
        /// </summary>
        public static PlotDeviceDetails GetDeviceDetails(string deviceName)
        {
            if (string.IsNullOrWhiteSpace(deviceName) || IsNoneDevice(deviceName)) return null;

            string key = deviceName.Trim();
            lock (CacheLock)
            {
                if (DetailsCache.TryGetValue(key, out PlotDeviceDetails cached)) return cached;
            }

            PlotDeviceDetails details;
            try
            {
                PlotConfig config = PlotConfigManager.SetCurrentConfig(key);
                if (config == null) return null;

                string location = Safe(() => config.LocationName);
                if (string.IsNullOrWhiteSpace(location)) location = Safe(() => config.PortName);

                details = new PlotDeviceDetails(
                    key,
                    Safe(() => config.DriverName),
                    location,
                    Safe(() => config.Comment),
                    MapPlotToFile(config),
                    Safe(() => config.DefaultFileExtension),
                    SafeInt(() => config.MaximumDeviceDotsPerInch));
            }
            catch (Exception)
            {
                return null;
            }

            lock (CacheLock)
            {
                DetailsCache[key] = details;
            }

            return details;
        }

        /// <summary>
        /// Paper sizes of <paramref name="deviceName"/> in the device's own order, with the names
        /// the Plot dialog shows. Uses the same sequence as the plot engine
        /// (<c>SetPlotConfigurationName</c> → <c>RefreshLists</c> → <c>GetCanonicalMediaNameList</c>).
        /// Empty when the device is missing or AutoCAD rejects it.
        /// </summary>
        public static PlotDeviceMedia GetMedia(string deviceName)
        {
            if (string.IsNullOrWhiteSpace(deviceName) || IsNoneDevice(deviceName))
                return PlotDeviceMedia.Empty;

            string key = deviceName.Trim();
            lock (CacheLock)
            {
                if (MediaCache.TryGetValue(key, out PlotDeviceMedia cached)) return cached;
            }

            PlotDeviceMedia media;
            try
            {
                PlotSettingsValidator validator = PlotSettingsValidator.Current;
                using (var settings = new PlotSettings(false))
                {
                    validator.SetPlotConfigurationName(settings, key, null);
                    validator.RefreshLists(settings);

                    string defaultMedia = Safe(() => settings.CanonicalMediaName);
                    StringCollection canonical = validator.GetCanonicalMediaNameList(settings);

                    var items = new List<PlotMediaInfo>(canonical?.Count ?? 0);
                    if (canonical != null)
                    {
                        for (int i = 0; i < canonical.Count; i++)
                        {
                            string name = canonical[i];
                            if (string.IsNullOrWhiteSpace(name)) continue;

                            int index = i;
                            items.Add(new PlotMediaInfo(name, Safe(() => validator.GetLocaleMediaName(settings, index))));
                        }
                    }

                    media = new PlotDeviceMedia(items, defaultMedia);
                }
            }
            catch (Exception)
            {
                return PlotDeviceMedia.Empty;
            }

            lock (CacheLock)
            {
                MediaCache[key] = media;
            }

            return media;
        }

        private static Dictionary<string, PlotDeviceKind> ReadDeviceKinds()
        {
            var kinds = new Dictionary<string, PlotDeviceKind>(StringComparer.OrdinalIgnoreCase);

            try
            {
                foreach (PlotConfigInfo info in PlotConfigManager.Devices)
                {
                    if (info == null || string.IsNullOrWhiteSpace(info.DeviceName)) continue;
                    kinds[info.DeviceName] = info.DeviceType == DeviceType.SystemPrinter
                        ? PlotDeviceKind.SystemPrinter
                        : PlotDeviceKind.Pc3;
                }
            }
            catch (Exception)
            {
                // Sin la colección se deduce el tipo por la extensión .pc3.
            }

            return kinds;
        }

        private static PlotToFileMode MapPlotToFile(PlotConfig config)
        {
            try
            {
                switch (config.PlotToFileCapability)
                {
                    case PlotToFileCapability.MustPlotToFile: return PlotToFileMode.Always;
                    case PlotToFileCapability.PlotToFileAllowed: return PlotToFileMode.Optional;
                    default: return PlotToFileMode.Never;
                }
            }
            catch (Exception)
            {
                return PlotToFileMode.Optional;
            }
        }

        private static string Safe(Func<string> read)
        {
            try { return read() ?? string.Empty; }
            catch (Exception) { return string.Empty; }
        }

        private static int SafeInt(Func<int> read)
        {
            try { return read(); }
            catch (Exception) { return 0; }
        }

        /// <summary>
        /// Copies the bundled HQ .pc3 into AutoCAD's plotters folder and returns the installed device
        /// name. Throws <see cref="PlotDeviceSetupException"/> when the asset or destination is missing.
        /// </summary>
        public static string InstallBundledHqPlotter(bool overwriteExisting = true)
        {
            string source = FindBundledHqPlotterPath();
            if (string.IsNullOrEmpty(source))
            {
                throw new PlotDeviceSetupException(
                    "No se encontró el plotter HQ empaquetado (\"" + BundledHqDeviceFileName + "\"). " +
                    "Colócalo en deploy/plotters/ (desarrollo) o en la carpeta Plotters junto al complemento.");
            }

            string folder = GetPlotterConfigFolder();
            if (string.IsNullOrWhiteSpace(folder))
            {
                throw new PlotDeviceSetupException(
                    "No se pudo determinar la carpeta de plotters de AutoCAD. " +
                    "Copia el .pc3 manualmente desde Opciones → Archivos → Ruta de configuración de impresora.");
            }

            string destination = Path.Combine(folder, BundledHqDeviceFileName);
            if (File.Exists(destination) && !overwriteExisting)
            {
                Refresh();
                return BundledHqDeviceFileName;
            }

            try
            {
                File.Copy(source, destination, overwrite: true);
            }
            catch (Exception ex)
            {
                throw new PlotDeviceSetupException(
                    "No se pudo copiar el plotter HQ a \"" + folder + "\": " + ex.Message, ex);
            }

            Refresh();
            return BundledHqDeviceFileName;
        }

        /// <summary>Looks for the bundled HQ file next to the add-in or under deploy/plotters.</summary>
        public static string FindBundledHqPlotterPath()
        {
            var candidates = new List<string>();

            try
            {
                string asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (!string.IsNullOrEmpty(asmDir))
                {
                    candidates.Add(Path.Combine(asmDir, "Plotters", BundledHqDeviceFileName));
                    candidates.Add(Path.Combine(asmDir, BundledHqDeviceFileName));
                    // ApplicationPlugins\GvrTools.bundle\Contents\2024\ → ..\..\Plotters\
                    string bundle = Path.GetFullPath(Path.Combine(asmDir, "..", ".."));
                    candidates.Add(Path.Combine(bundle, "Plotters", BundledHqDeviceFileName));
                }
            }
            catch (Exception)
            {
                // Ignore and try repo-relative paths below.
            }

            // Dev checkout: .../src/GvrTools.Civil3D/bin/... → repo deploy/plotters
            try
            {
                string asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (!string.IsNullOrEmpty(asmDir))
                {
                    DirectoryInfo dir = new DirectoryInfo(asmDir);
                    for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
                    {
                        string deploy = Path.Combine(dir.FullName, "deploy", "plotters", BundledHqDeviceFileName);
                        candidates.Add(deploy);
                    }
                }
            }
            catch (Exception)
            {
            }

            foreach (string path in candidates)
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    return path;
            }

            return null;
        }

        public static bool BundledHqPlotterAvailable => !string.IsNullOrEmpty(FindBundledHqPlotterPath());

        /// <summary>Folder where AutoCAD stores .pc3 files (PrinterConfigPath).</summary>
        public static string GetPlotterConfigFolder()
        {
            try
            {
                string folder = ReadPreferenceString("Files", "PrinterConfigPath");
                if (!string.IsNullOrWhiteSpace(folder))
                {
                    foreach (string candidate in folder.Split(';'))
                    {
                        string trimmed = candidate.Trim();
                        if (!string.IsNullOrEmpty(trimmed) && Directory.Exists(trimmed))
                            return trimmed;
                    }
                }
            }
            catch (Exception)
            {
            }

            return FindPlotterFolderByConvention();
        }

        private static string ReadPreferenceString(string sectionName, string propertyName)
        {
            try
            {
                object preferences = Autodesk.AutoCAD.ApplicationServices.Application.Preferences;
                if (preferences == null) return null;

                object section = preferences.GetType()
                    .GetProperty(sectionName)?
                    .GetValue(preferences, null);
                if (section == null) return null;

                return section.GetType()
                    .GetProperty(propertyName)?
                    .GetValue(section, null) as string;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string FindPlotterFolderByConvention()
        {
            try
            {
                string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string autodesk = Path.Combine(roaming, "Autodesk");
                if (!Directory.Exists(autodesk)) return null;

                string wanted = "C3D " + C3DVersionInfo.CompiledFor.ToString(
                    System.Globalization.CultureInfo.InvariantCulture);

                string release = Path.Combine(autodesk, wanted);
                if (!Directory.Exists(release)) return null;

                foreach (string language in Directory.GetDirectories(release))
                {
                    string candidate = Path.Combine(language, "Plotters");
                    if (Directory.Exists(candidate))
                        return candidate;
                }

                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
