using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using GvrTools.Core.Naming;

namespace GvrTools.Civil3D.Model
{
    /// <summary>
    /// Document-level information: the output subfolder name and the drawing-wide tokens.
    /// Equivalent to Revit's <c>ProjectSnapshot</c>, scoped to one open DWG.
    /// </summary>
    public sealed class DrawingSnapshot
    {
        private DrawingSnapshot(string title, string localFolder, string drawingKey)
        {
            Title = title;
            LocalFolder = localFolder;
            DrawingKey = drawingKey;
        }

        /// <summary>DWG file title (without extension), used as the name of the folder each run writes into.</summary>
        public string Title { get; }

        /// <summary>Folder the drawing lives in, or null for an unsaved drawing. Used as a default destination.</summary>
        public string LocalFolder { get; }

        /// <summary>
        /// Stable, filename-safe identifier for this specific drawing file, used to key the
        /// per-drawing export-history store: a short hash of the full path (so two drawings with
        /// the same title in different folders never collide) plus a readable, sanitised prefix.
        /// </summary>
        public string DrawingKey { get; }

        public static DrawingSnapshot Read(string fullPath)
        {
            string title = string.IsNullOrWhiteSpace(fullPath)
                ? "Dibujo"
                : Path.GetFileNameWithoutExtension(fullPath);

            string folder = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(fullPath) && File.Exists(fullPath))
                    folder = Path.GetDirectoryName(fullPath);
            }
            catch (Exception)
            {
                // Se ignora: un dibujo sin guardar no tiene ruta local usable.
            }

            return new DrawingSnapshot(title, folder, ComputeDrawingKey(fullPath, title));
        }

        /// <summary>Values for the drawing-scoped file-name tokens, shared by every layout in a run.</summary>
        public IReadOnlyDictionary<string, string> ToTokens() => new Dictionary<string, string>
        {
            ["DrawingTitle"] = Title,
            ["Date"] = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        };

        private static string ComputeDrawingKey(string fullPath, string title)
        {
            string identitySource = string.IsNullOrWhiteSpace(fullPath) ? title : fullPath;

            string hash;
            using (MD5 md5 = MD5.Create())
            {
                byte[] bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(identitySource));
                var sb = new StringBuilder(8);
                for (int i = 0; i < 4; i++) sb.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
                hash = sb.ToString();
            }

            return PathSanitizer.SanitizeFileName(title) + "-" + hash;
        }
    }
}
