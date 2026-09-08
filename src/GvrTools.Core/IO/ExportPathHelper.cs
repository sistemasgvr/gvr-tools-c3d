using System;
using System.IO;
using System.Security;

namespace GvrTools.Core.IO
{
    /// <summary>
    /// Validates and resolves export destination folders so we fail early with a clear message
    /// instead of hitting "Access denied" mid-batch (e.g. under Program Files).
    /// </summary>
    public static class ExportPathHelper
    {
        public static string DefaultExportRoot =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "GVR Tools",
                "Exportaciones");

        /// <summary>
        /// Returns the first writable candidate, or creates and returns <see cref="DefaultExportRoot"/>.
        /// </summary>
        public static string ResolveWritableFolder(params string[] candidates)
        {
            if (candidates != null)
            {
                foreach (string candidate in candidates)
                {
                    if (string.IsNullOrWhiteSpace(candidate))
                        continue;

                    string path = candidate.Trim();
                    if (TryEnsureWritable(path, out _))
                        return path;
                }
            }

            string fallback = DefaultExportRoot;
            TryEnsureWritable(fallback, out _);
            return fallback;
        }

        public static bool IsWritableDirectory(string path, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(path))
            {
                error = "No se indicó una carpeta de destino.";
                return false;
            }

            path = path.Trim();

            try
            {
                string probeRoot = Directory.Exists(path)
                    ? path
                    : ResolveExistingAncestor(path);

                if (string.IsNullOrEmpty(probeRoot))
                {
                    error = "La ruta no es válida o no se puede acceder.";
                    return false;
                }

                string probe = Path.Combine(probeRoot, $".gvr-write-test-{Guid.NewGuid():N}.tmp");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                error = FormatAccessDenied(path);
                return false;
            }
            catch (SecurityException)
            {
                error = FormatAccessDenied(path);
                return false;
            }
            catch (IOException ex)
            {
                error = ex.Message;
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static bool TryEnsureWritable(string path, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(path))
            {
                error = "No se indicó una carpeta de destino.";
                return false;
            }

            path = path.Trim();

            try
            {
                Directory.CreateDirectory(path);
            }
            catch (UnauthorizedAccessException)
            {
                error = FormatAccessDenied(path);
                return false;
            }
            catch (Exception ex)
            {
                error =
                    $"No se pudo crear la carpeta de destino:{Environment.NewLine}{path}" +
                    $"{Environment.NewLine}{Environment.NewLine}{ex.Message}";
                return false;
            }

            if (!IsWritableDirectory(path, out error))
                return false;

            return true;
        }

        /// <summary>
        /// Si <paramref name="desiredPath"/> ya existe (carpeta o archivo), devuelve un hermano
        /// libre con sufijo estilo Explorer: <c>Nombre (1)</c>, <c>Nombre (2)</c>, …
        /// No crea la carpeta; solo elige el nombre. Así un lote nuevo no mezcla archivos con uno anterior.
        /// </summary>
        public static string AllocateUniqueDirectoryPath(string desiredPath)
        {
            if (string.IsNullOrWhiteSpace(desiredPath))
                return desiredPath;

            desiredPath = desiredPath.Trim();
            if (!Directory.Exists(desiredPath) && !File.Exists(desiredPath))
                return desiredPath;

            string parent = Path.GetDirectoryName(desiredPath);
            string name = Path.GetFileName(desiredPath);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
                return desiredPath;

            for (int n = 1; n < 10000; n++)
            {
                string candidate = Path.Combine(parent, name + " (" + n.ToString() + ")");
                if (!Directory.Exists(candidate) && !File.Exists(candidate))
                    return candidate;
            }

            // Extremadamente improbable; evita un bucle infinito.
            return Path.Combine(parent, name + " (" + Guid.NewGuid().ToString("N") + ")");
        }

        private static string ResolveExistingAncestor(string path)
        {
            string current = path;
            while (!string.IsNullOrEmpty(current))
            {
                if (Directory.Exists(current))
                    return current;

                current = Path.GetDirectoryName(current);
            }

            return null;
        }

        private static string FormatAccessDenied(string path)
        {
            return
                $"No hay permiso para escribir en:{Environment.NewLine}{path}{Environment.NewLine}{Environment.NewLine}" +
                "Elige otra carpeta (por ejemplo Documentos\\GVR Tools\\Exportaciones). " +
                "Las carpetas dentro de Program Files o de la instalación de Revit suelen estar protegidas.";
        }
    }
}
