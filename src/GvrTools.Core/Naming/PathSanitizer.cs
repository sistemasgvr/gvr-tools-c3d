using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace GvrTools.Core.Naming
{
    /// <summary>
    /// Turns arbitrary text (sheet names, project titles) into something Windows will actually
    /// accept as a file or folder name.
    /// </summary>
    public static class PathSanitizer
    {
        /// <summary>Longest name we produce, leaving room for a folder path and an extension.</summary>
        public const int MaxNameLength = 120;

        /// <summary>Windows refuses these as file names even when an extension is added.</summary>
        private static readonly HashSet<string> ReservedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        private static readonly char[] InvalidFileChars = Path.GetInvalidFileNameChars();

        public static string SanitizeFileName(string value, string fallback = "sin-nombre")
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;

            var sb = new StringBuilder(value.Length);
            foreach (char c in value)
                sb.Append(InvalidFileChars.Contains(c) || char.IsControl(c) ? '_' : c);

            // Windows silently drops trailing dots and spaces, which makes two names that differ
            // only by them collide in confusing ways; normalise that away up front.
            string result = CollapseWhitespace(sb.ToString()).Trim().TrimEnd('.', ' ');

            if (result.Length > MaxNameLength)
                result = result.Substring(0, MaxNameLength).TrimEnd('.', ' ');

            if (result.Length == 0) return fallback;

            return ReservedNames.Contains(Path.GetFileNameWithoutExtension(result)) ? "_" + result : result;
        }

        public static string SanitizeFolderName(string value, string fallback = "Proyecto") =>
            SanitizeFileName(value, fallback);

        private static string CollapseWhitespace(string value)
        {
            var sb = new StringBuilder(value.Length);
            bool previousWasSpace = false;

            foreach (char c in value)
            {
                bool isSpace = char.IsWhiteSpace(c);
                if (isSpace && previousWasSpace) continue;

                sb.Append(isSpace ? ' ' : c);
                previousWasSpace = isSpace;
            }

            return sb.ToString();
        }
    }
}
