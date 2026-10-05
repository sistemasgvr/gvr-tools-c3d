using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace GvrTools.Core.IO
{
    /// <summary>
    /// Helpers for AutoCAD canonical media names ("ISO_full_bleed_A1_(841.00_x_594.00_MM)").
    /// Pure string logic (no AutoCAD dependency).
    /// </summary>
    public static class PlotMediaNames
    {
        /// <summary>
        /// Readable fallback for a canonical name when the device's localized name is not at hand:
        /// underscores become spaces ("ISO full bleed A1 (841.00 x 594.00 MM)").
        /// </summary>
        public static string Humanize(string canonicalMediaName)
        {
            if (string.IsNullOrWhiteSpace(canonicalMediaName)) return string.Empty;

            string text = Regex.Replace(canonicalMediaName.Replace('_', ' '), @"\s+", " ");
            return text.Replace("( ", "(").Replace(" )", ")").Trim();
        }

        /// <summary>
        /// Reads the "(W x H UNIT)" pair from a canonical name. Returns false for names without
        /// dimensions ("Letter", "A4" from a Windows printer driver).
        /// </summary>
        public static bool TryGetDimensions(string canonicalMediaName, out double width, out double height)
        {
            width = 0;
            height = 0;
            if (string.IsNullOrEmpty(canonicalMediaName)) return false;

            int open = canonicalMediaName.LastIndexOf('(');
            int close = canonicalMediaName.LastIndexOf(')');
            if (open < 0 || close <= open) return false;

            string inside = canonicalMediaName.Substring(open + 1, close - open - 1).Replace('_', ' ');
            int x = inside.IndexOf(" x ", StringComparison.OrdinalIgnoreCase);
            if (x < 0) return false;

            string left = FirstNumber(inside.Substring(0, x));
            string right = FirstNumber(inside.Substring(x + 3));

            return double.TryParse(left, NumberStyles.Float, CultureInfo.InvariantCulture, out width) &&
                   double.TryParse(right, NumberStyles.Float, CultureInfo.InvariantCulture, out height) &&
                   width > 0 && height > 0;
        }

        /// <summary>
        /// Like <see cref="TryGetDimensions"/> but converted to millimetres ("Inches" × 25.4).
        /// Returns false for pixel media and names without dimensions.
        /// </summary>
        public static bool TryGetSizeInMillimeters(string canonicalMediaName, out double width, out double height)
        {
            if (!TryGetDimensions(canonicalMediaName, out width, out height)) return false;

            string unit = canonicalMediaName.Substring(canonicalMediaName.LastIndexOf('(')).ToUpperInvariant();
            if (unit.Contains("PIXEL")) return false;

            if (unit.Contains("INCH") || unit.Contains("_IN)") || unit.Contains(" IN)"))
            {
                width *= 25.4;
                height *= 25.4;
            }

            return true;
        }

        private static string FirstNumber(string text)
        {
            var sb = new StringBuilder();
            foreach (char c in text.Trim())
            {
                if (char.IsDigit(c) || c == '.') sb.Append(c);
                else if (sb.Length > 0) break;
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// AutoCAD stores the drawing orientation as a rotation relative to the media as the device
    /// defines it (0/90/180/270°), while the Plot dialog shows Portrait/Landscape + "upside-down".
    /// "Landscape" means the long edge of the paper is the top of the page, so the rotation that
    /// produces it depends on whether the media itself is defined landscape (841 x 594) or
    /// portrait (8.5 x 11). Pure math, shared by the exporter and the page-setup reader.
    /// </summary>
    public static class PlotOrientationMath
    {
        /// <summary>Quarter turns (0 = 0°, 1 = 90°, 2 = 180°, 3 = 270°).</summary>
        public static int ToQuarterTurns(bool landscape, bool upsideDown, bool mediaIsLandscape)
        {
            int turns = landscape == mediaIsLandscape ? 0 : 1;
            return upsideDown ? turns + 2 : turns;
        }

        public static void FromQuarterTurns(int quarterTurns, bool mediaIsLandscape, out bool landscape, out bool upsideDown)
        {
            int turns = ((quarterTurns % 4) + 4) % 4;
            upsideDown = turns >= 2;
            bool rotated = turns % 2 == 1;
            landscape = rotated ? !mediaIsLandscape : mediaIsLandscape;
        }
    }
}
