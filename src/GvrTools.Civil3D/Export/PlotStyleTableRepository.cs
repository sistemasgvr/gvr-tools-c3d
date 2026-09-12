using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using Autodesk.AutoCAD.DatabaseServices;

namespace GvrTools.Civil3D.Export
{
    /// <summary>
    /// Reads the plot style tables (.ctb / .stb) AutoCAD actually has available, the same list the
    /// "Plot style table (pen assignments)" combo of the Plot dialog offers.
    ///
    /// A layout can reference a table that is not installed on this machine — the Plot dialog shows
    /// it as "Lombardi.ctb (missing)" — in which case plotting silently falls back to no pen
    /// assignments at all and the PDF comes out with the wrong lineweights and colours. Listing the
    /// real tables lets the batch exporter offer a replacement instead of failing quietly.
    /// </summary>
    public static class PlotStyleTableRepository
    {
        /// <summary>Shown in the UI to mean "leave each layout's own table alone".</summary>
        public const string KeepLayoutTableLabel = "(usar la del layout)";

        // Asking AutoCAD for the list hits the plot-configuration subsystem, and IsInstalled is
        // called once per layout of every drawing in a batch. The set of installed .ctb/.stb files
        // does not change while a run is in flight, so it is read once and reused; Refresh() exists
        // for the rare case of a table being added mid-session.
        private static IReadOnlyList<string> _cachedTables;
        private static HashSet<string> _cachedLookup;
        private static readonly object CacheLock = new object();

        /// <summary>
        /// Every installed plot style table, sorted, or an empty list when AutoCAD cannot be asked
        /// (no running host). Never throws: an empty combo is better than a broken window.
        /// </summary>
        public static IReadOnlyList<string> GetAvailableTables()
        {
            lock (CacheLock)
            {
                if (_cachedTables != null) return _cachedTables;

                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                bool answered = false;

                try
                {
                    StringCollection list = PlotSettingsValidator.Current.GetPlotStyleSheetList();
                    foreach (string name in list)
                    {
                        if (!string.IsNullOrWhiteSpace(name))
                            names.Add(name);
                    }

                    answered = true;
                }
                catch (Exception)
                {
                    // Sin host de AutoCAD no hay lista; abajo se intenta leer la carpeta igualmente.
                }

                // AutoCAD construye GetPlotStyleSheetList una sola vez y no la reconstruye cuando
                // aparece un archivo nuevo en la carpeta, así que una tabla recién instalada seguiría
                // ausente de la lista (y del combo) hasta reiniciar. Leer la carpeta cubre ese hueco.
                if (AddTablesFromDisk(names)) answered = true;

                if (!answered)
                {
                    // Ni AutoCAD ni el disco respondieron: no se cachea para que un intento posterior
                    // sí pueda obtenerla.
                    return new List<string>();
                }

                var result = new List<string>(names);
                result.Sort(StringComparer.OrdinalIgnoreCase);

                _cachedTables = result;
                _cachedLookup = new HashSet<string>(result, StringComparer.OrdinalIgnoreCase);
                return _cachedTables;
            }
        }

        /// <summary>Keeps the source file's extension when the requested name has none of its own.</summary>
        private static string EnsureExtension(string name, string extension)
        {
            string trimmed = name.Trim();
            string existing = System.IO.Path.GetExtension(trimmed);

            if (string.Equals(existing, ".ctb", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(existing, ".stb", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed;
            }

            return trimmed + extension;
        }

        /// <summary>
        /// Adds the .ctb/.stb files sitting in AutoCAD's plot style folder. Returns true when the
        /// folder could be read at all (even if empty), so callers can tell "no tables" from
        /// "could not look".
        /// </summary>
        private static bool AddTablesFromDisk(HashSet<string> names)
        {
            try
            {
                string folder = GetPlotStyleFolder();
                if (string.IsNullOrWhiteSpace(folder) || !System.IO.Directory.Exists(folder))
                    return false;

                foreach (string file in System.IO.Directory.GetFiles(folder))
                {
                    string extension = System.IO.Path.GetExtension(file);
                    if (string.Equals(extension, ".ctb", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(extension, ".stb", StringComparison.OrdinalIgnoreCase))
                    {
                        names.Add(System.IO.Path.GetFileName(file));
                    }
                }

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Drops the cached list so the next read asks AutoCAD again.</summary>
        public static void Refresh()
        {
            lock (CacheLock)
            {
                _cachedTables = null;
                _cachedLookup = null;
            }
        }

        /// <summary>
        /// True when <paramref name="tableName"/> is a table AutoCAD can actually load. A layout
        /// pointing at a table that is not installed reports the name it wants, not whether it
        /// exists, so this is what separates "Lombardi.ctb" from "Lombardi.ctb (missing)".
        /// </summary>
        public static bool IsInstalled(string tableName)
        {
            if (string.IsNullOrWhiteSpace(tableName)) return false;

            GetAvailableTables();

            lock (CacheLock)
            {
                return _cachedLookup != null && _cachedLookup.Contains(tableName);
            }
        }

        /// <summary>
        /// Folder AutoCAD keeps its plot style tables in (the one the "Add Plot Style Table" wizard
        /// writes to), or null when it cannot be resolved.
        /// </summary>
        public static string GetPlotStyleFolder()
        {
            try
            {
                // There is no system variable for this (PRINTSTYLEPATH does not exist — getvar
                // returns nil); the supported route is the Files section of the application
                // preferences, which is also what the Options dialog edits. Reached by reflection
                // rather than `dynamic`: the preferences objects are COM interfaces whose managed
                // wrappers differ per release, and `dynamic` needs Microsoft.CSharp, which is not
                // available on the .NET Framework target this also builds for.
                string folder = ReadPreferenceString("Files", "PrinterStyleSheetPath");

                // The preference can hold several ";"-separated search paths; the first writable one
                // is where the Add Plot Style Table wizard puts new tables.
                if (!string.IsNullOrWhiteSpace(folder))
                {
                    foreach (string candidate in folder.Split(';'))
                    {
                        string trimmed = candidate.Trim();
                        if (!string.IsNullOrEmpty(trimmed) && System.IO.Directory.Exists(trimmed))
                            return trimmed;
                    }
                }
            }
            catch (Exception)
            {
                // Cae al descubrimiento por convención de abajo.
            }

            return FindPlotStyleFolderByConvention();
        }

        /// <summary>
        /// Installs <paramref name="sourceFile"/> (a .ctb/.stb living anywhere, typically next to the
        /// project's drawings) into AutoCAD's plot style folder so the plot engine can resolve it by
        /// name — <see cref="PlotSettingsValidator.SetCurrentStyleSheet"/> only accepts registered
        /// table NAMES, never file paths, which is why a table shipped with a project cannot be used
        /// until it is copied where AutoCAD looks.
        /// </summary>
        /// <param name="installAs">
        /// File name to install it under, instead of the source file's own. This is what lets a
        /// table the project ships as "Lombardi 3.ctb" satisfy layouts that ask for "Lombardi.ctb":
        /// the plot engine matches tables by name, so the same pens under the wrong name are still
        /// a missing table.
        /// </param>
        /// <returns>The table name to pass to the exporter (e.g. "Lombardi.ctb").</returns>
        /// <exception cref="PlotStyleImportException">The file is unusable or cannot be copied.</exception>
        public static string Import(string sourceFile, bool overwriteExisting, string installAs = null)
        {
            if (string.IsNullOrWhiteSpace(sourceFile) || !System.IO.File.Exists(sourceFile))
                throw new PlotStyleImportException("No se encontró el archivo de tabla de estilos seleccionado.");

            string extension = System.IO.Path.GetExtension(sourceFile);
            if (!string.Equals(extension, ".ctb", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(extension, ".stb", StringComparison.OrdinalIgnoreCase))
            {
                throw new PlotStyleImportException("El archivo debe ser una tabla de estilos .ctb o .stb.");
            }

            string folder = GetPlotStyleFolder();
            if (string.IsNullOrWhiteSpace(folder))
            {
                throw new PlotStyleImportException(
                    "No se pudo determinar la carpeta de tablas de estilos de AutoCAD. " +
                    "Cópiala manualmente desde el Administrador de estilos de trazado.");
            }

            string fileName = string.IsNullOrWhiteSpace(installAs)
                ? System.IO.Path.GetFileName(sourceFile)
                : EnsureExtension(installAs, extension);

            string destination = System.IO.Path.Combine(folder, fileName);

            // Ya instalada y en la misma ruta: no hay nada que copiar.
            if (string.Equals(
                    System.IO.Path.GetFullPath(sourceFile),
                    System.IO.Path.GetFullPath(destination),
                    StringComparison.OrdinalIgnoreCase))
            {
                Refresh();
                return fileName;
            }

            if (System.IO.File.Exists(destination) && !overwriteExisting)
                throw new PlotStyleAlreadyExistsException(fileName, destination);

            try
            {
                System.IO.File.Copy(sourceFile, destination, true);
            }
            catch (Exception ex)
            {
                throw new PlotStyleImportException(
                    $"No se pudo copiar la tabla de estilos a \"{folder}\": {ex.Message}", ex);
            }

            // La lista cacheada quedó obsoleta en cuanto apareció el archivo nuevo.
            Refresh();
            return fileName;
        }

        /// <summary>
        /// Reads one string property out of an application-preferences section (e.g. "Files" /
        /// "PrinterStyleSheetPath"), or null when it is not reachable on this host.
        /// </summary>
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

        /// <summary>
        /// Falls back to the standard roaming location when the preferences cannot be read.
        ///
        /// Anchored to the release this binary was built for (one DLL is installed per Civil 3D
        /// version, so that is the version actually running): a machine with 2025 and 2027 side by
        /// side has one "Plot Styles" folder each, and installing the table into the wrong one would
        /// leave it just as missing as before.
        /// </summary>
        private static string FindPlotStyleFolderByConvention()
        {
            try
            {
                string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string autodesk = System.IO.Path.Combine(roaming, "Autodesk");
                if (!System.IO.Directory.Exists(autodesk)) return null;

                string wanted = "C3D " + Infrastructure.C3DVersionInfo.CompiledFor.ToString(
                    System.Globalization.CultureInfo.InvariantCulture);

                string release = System.IO.Path.Combine(autodesk, wanted);
                if (!System.IO.Directory.Exists(release)) return null;

                // Debajo del release hay una carpeta por idioma instalado ("esp", "enu"...).
                foreach (string language in System.IO.Directory.GetDirectories(release))
                {
                    string candidate = System.IO.Path.Combine(language, "Plotters", "Plot Styles");
                    if (System.IO.Directory.Exists(candidate))
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
