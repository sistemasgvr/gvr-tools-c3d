using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Reflection;
using Autodesk.AutoCAD.DatabaseServices;
using GvrTools.Civil3D.Infrastructure;

namespace GvrTools.Civil3D.Export
{
    /// <summary>
    /// Lists plot devices (.pc3 / system printers) AutoCAD exposes via
    /// <see cref="PlotSettingsValidator.GetPlotDeviceList"/>, filtered to PDF ePlot devices.
    /// Also installs a bundled high-quality PC3 into the plotters folder when present.
    /// </summary>
    public static class PlotDeviceRepository
    {
        public const string DefaultPdfDeviceName = "DWG To PDF.pc3";

        /// <summary>File name of the optional high-DPI plotter shipped next to the add-in.</summary>
        public const string BundledHqDeviceFileName = "DWG To PDF_HQ_.pc3";

        private static IReadOnlyList<string> _cachedDevices;
        private static HashSet<string> _cachedLookup;
        private static readonly object CacheLock = new object();

        public static IReadOnlyList<string> GetAvailablePdfDevices()
        {
            lock (CacheLock)
            {
                if (_cachedDevices != null) return _cachedDevices;

                var names = new List<string>();
                try
                {
                    StringCollection list = PlotSettingsValidator.Current.GetPlotDeviceList();
                    foreach (string name in list)
                    {
                        if (IsUsablePdfDevice(name))
                            names.Add(name);
                    }
                }
                catch (Exception)
                {
                    return new List<string>();
                }

                names.Sort(StringComparer.OrdinalIgnoreCase);
                _cachedDevices = names;
                _cachedLookup = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
                return _cachedDevices;
            }
        }

        public static void Refresh()
        {
            lock (CacheLock)
            {
                _cachedDevices = null;
                _cachedLookup = null;
            }
        }

        public static bool IsInstalled(string deviceName)
        {
            if (string.IsNullOrWhiteSpace(deviceName)) return false;
            GetAvailablePdfDevices();
            lock (CacheLock)
            {
                return _cachedLookup != null && _cachedLookup.Contains(deviceName.Trim());
            }
        }

        public static string ResolveDefaultDevice()
        {
            IReadOnlyList<string> devices = GetAvailablePdfDevices();
            if (devices.Count == 0) return string.Empty;

            foreach (string device in devices)
            {
                if (string.Equals(device, DefaultPdfDeviceName, StringComparison.OrdinalIgnoreCase))
                    return device;
            }

            return devices[0];
        }

        public static bool IsUsablePdfDevice(string plotConfigurationName)
        {
            return !string.IsNullOrWhiteSpace(plotConfigurationName) &&
                plotConfigurationName.IndexOf("pdf", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Lists canonical media names for <paramref name="deviceName"/> via
        /// <c>SetPlotConfigurationName</c> → <c>RefreshLists</c> → <c>GetCanonicalMediaNameList</c>
        /// (same sequence as the plot engine). Returns empty if the device is missing or AutoCAD rejects it.
        /// </summary>
        public static IReadOnlyList<string> GetCanonicalMediaNames(string deviceName)
        {
            if (string.IsNullOrWhiteSpace(deviceName))
                return Array.Empty<string>();

            try
            {
                PlotSettingsValidator validator = PlotSettingsValidator.Current;
                using (var settings = new PlotSettings(false))
                {
                    validator.SetPlotConfigurationName(settings, deviceName.Trim(), null);
                    validator.RefreshLists(settings);

                    StringCollection mediaCollection = validator.GetCanonicalMediaNameList(settings);
                    if (mediaCollection == null || mediaCollection.Count == 0)
                        return Array.Empty<string>();

                    var names = new List<string>(mediaCollection.Count);
                    foreach (string media in mediaCollection)
                    {
                        if (!string.IsNullOrWhiteSpace(media))
                            names.Add(media);
                    }

                    names.Sort(StringComparer.OrdinalIgnoreCase);
                    return names;
                }
            }
            catch (Exception)
            {
                return Array.Empty<string>();
            }
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
