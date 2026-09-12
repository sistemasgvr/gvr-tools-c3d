using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using Autodesk.AutoCAD.DatabaseServices;

namespace GvrTools.Civil3D.Export
{
    /// <summary>
    /// Lists plot devices (.pc3 / system printers) AutoCAD exposes via
    /// <see cref="PlotSettingsValidator.GetPlotDeviceList"/>, filtered to PDF ePlot devices suitable
    /// for batch export. Mirrors <see cref="PlotStyleTableRepository"/> for CTB/STB.
    /// </summary>
    public static class PlotDeviceRepository
    {
        public const string DefaultPdfDeviceName = "DWG To PDF.pc3";

        private static IReadOnlyList<string> _cachedDevices;
        private static HashSet<string> _cachedLookup;
        private static readonly object CacheLock = new object();

        /// <summary>
        /// Installed PDF plot devices, sorted, or empty when AutoCAD cannot be asked. Never throws.
        /// </summary>
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

        /// <summary>Clears the cache so a newly installed .pc3 appears without restarting AutoCAD.</summary>
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

            IReadOnlyList<string> devices = GetAvailablePdfDevices();
            lock (CacheLock)
            {
                return _cachedLookup != null && _cachedLookup.Contains(deviceName.Trim());
            }
        }

        /// <summary>
        /// Prefer <see cref="DefaultPdfDeviceName"/> when installed; otherwise the first PDF device,
        /// or empty when none exist.
        /// </summary>
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

        /// <summary>
        /// A device is safe for PDF batch export when its name clearly identifies a PDF ePlot
        /// configuration (custom HQ copies usually keep "PDF" in the name, e.g. "DWG To PDF HQ.pc3").
        /// </summary>
        public static bool IsUsablePdfDevice(string plotConfigurationName)
        {
            return !string.IsNullOrWhiteSpace(plotConfigurationName) &&
                plotConfigurationName.IndexOf("pdf", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
