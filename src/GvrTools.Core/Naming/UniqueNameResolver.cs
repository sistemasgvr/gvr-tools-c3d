using System;
using System.Collections.Generic;
using System.IO;

namespace GvrTools.Core.Naming
{
    /// <summary>
    /// Hands out collision-free names inside one folder, appending "_2", "_3" and so on.
    ///
    /// Unlike a bare File.Exists check it also remembers what it already handed out during the
    /// current run. That matters because some export APIs write their file only after the call
    /// returns, so two sheets whose names sanitise to the same string would otherwise both be
    /// considered free and one file would silently overwrite the other.
    /// </summary>
    public sealed class UniqueNameResolver
    {
        private readonly HashSet<string> _reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly string _folder;
        private readonly Func<string, bool> _exists;

        /// <param name="folder">Folder the names live in.</param>
        /// <param name="exists">Existence probe; defaults to a locked-file-aware check and is swapped in tests.</param>
        public UniqueNameResolver(string folder, Func<string, bool> exists = null)
        {
            _folder = folder ?? string.Empty;
            _exists = exists ?? IsPathTaken;
        }

        /// <summary>
        /// Reserves and returns a base name (no extension) that is free both on disk and within
        /// this run. Use it for export APIs that append the extension themselves.
        /// </summary>
        public string ReserveBaseName(string baseName, string extension) =>
            ReserveBaseName(baseName, extension, null);

        /// <summary>
        /// Same as <see cref="ReserveBaseName(string,string)"/> but also avoids Revit DWG sibling
        /// files named <c>{baseName}-{view}.ext</c> when views are exported separately.
        /// </summary>
        /// <summary>Hard ceiling on "_2", "_3", ... attempts -- matches ExportPathHelper.AllocateUniqueDirectoryPath's cap.</summary>
        private const int MaxAttempts = 10000;

        public string ReserveBaseName(string baseName, string extension, IReadOnlyList<string> viewSuffixes)
        {
            string ext = NormalizeExtension(extension);

            // Sanea los sufijos de vista para que sean únicos ENTRE SÍ antes de reservar nada. Sin
            // esto, dos vistas colocadas en la misma lámina cuyo View.Name sanea al mismo string (p.
            // ej. "Detalle 1/2" y "Detalle 1:2", donde PathSanitizer convierte ambos caracteres
            // inválidos a "_") hacían que TryReserve fallara SIEMPRE para cualquier candidate -- la
            // colisión es entre los dos sufijos duplicados, no depende del nombre base -- y el bucle de
            // abajo reintentaba para siempre sin ninguna combinación que pudiera tener éxito jamás.
            IReadOnlyList<string> uniqueViewSuffixes = MakeSuffixesUnique(viewSuffixes);

            string candidate = baseName;
            int suffix = 2;
            while (!TryReserve(candidate, ext, uniqueViewSuffixes))
            {
                if (suffix > MaxAttempts)
                {
                    throw new InvalidOperationException(
                        $"No se pudo reservar un nombre único para \"{baseName}\" en \"{_folder}\" tras {MaxAttempts} intentos.");
                }

                candidate = baseName + "_" + suffix.ToString();
                suffix++;
            }

            return candidate;
        }

        /// <summary>
        /// Returns a copy of <paramref name="viewSuffixes"/> where any suffix that sanitizes/repeats
        /// identically to an earlier one in the same list gets its own "_2", "_3", ... appended, so no
        /// two entries can ever collide with each other regardless of what the sheet's base name is.
        /// </summary>
        private static IReadOnlyList<string> MakeSuffixesUnique(IReadOnlyList<string> viewSuffixes)
        {
            if (viewSuffixes == null || viewSuffixes.Count == 0)
                return viewSuffixes;

            var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>(viewSuffixes.Count);

            foreach (string raw in viewSuffixes)
            {
                if (string.IsNullOrWhiteSpace(raw))
                {
                    result.Add(raw);
                    continue;
                }

                string trimmed = raw.Trim();
                if (!seen.TryGetValue(trimmed, out int count))
                {
                    seen[trimmed] = 1;
                    result.Add(trimmed);
                }
                else
                {
                    count++;
                    seen[trimmed] = count;
                    result.Add(trimmed + "_" + count.ToString());
                }
            }

            return result;
        }

        /// <summary>Reserves a name and returns the full path it maps to.</summary>
        public string ReservePath(string baseName, string extension) =>
            Path.Combine(_folder, ReserveBaseName(baseName, extension) + NormalizeExtension(extension));

        private bool TryReserve(string candidate, string extension, IReadOnlyList<string> viewSuffixes)
        {
            if (!TryReserveFileName(candidate + extension))
                return false;

            if (viewSuffixes == null || viewSuffixes.Count == 0)
                return true;

            foreach (string viewSuffix in viewSuffixes)
            {
                if (string.IsNullOrWhiteSpace(viewSuffix))
                    continue;

                if (!TryReserveFileName(candidate + "-" + viewSuffix.Trim() + extension))
                    return false;
            }

            return true;
        }

        private bool TryReserveFileName(string fileName)
        {
            if (_reserved.Contains(fileName))
                return false;

            string fullPath = Path.Combine(_folder, fileName);
            if (_exists(fullPath))
                return false;

            // Revit DWG may emit {baseName}-{view}.ext even when only the main file was checked.
            if (Directory.Exists(_folder))
            {
                string stem = Path.GetFileNameWithoutExtension(fileName);
                string ext = Path.GetExtension(fileName);
                try
                {
                    foreach (string sibling in Directory.GetFiles(_folder, stem + "-*" + ext))
                    {
                        if (_exists(sibling))
                            return false;
                    }
                }
                catch (IOException)
                {
                    return false;
                }
            }

            _reserved.Add(fileName);
            return true;
        }

        private bool IsPathTaken(string fullPath)
        {
            if (!File.Exists(fullPath))
                return false;

            try
            {
                using (var stream = new FileStream(fullPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    return true;
                }
            }
            catch (IOException)
            {
                return true;
            }
        }

        private static string NormalizeExtension(string extension)
        {
            if (string.IsNullOrEmpty(extension))
                return string.Empty;

            return extension[0] == '.' ? extension : "." + extension;
        }
    }
}
