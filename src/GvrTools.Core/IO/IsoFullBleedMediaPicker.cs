using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace GvrTools.Core.IO
{
    /// <summary>
    /// Picks an ISO full-bleed canonical media name from a plot device's media list.
    /// Pure string logic (no AutoCAD dependency).
    /// </summary>
    public static class IsoFullBleedMediaPicker
    {
        private static readonly Regex IsoFullBleedPattern = new Regex(
            @"^ISO_full_bleed_A([0-4])_",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>
        /// Returns a media name for the requested ISO size, preferring landscape (width &gt; height
        /// in the parenthetical millimetre pair) when both orientations exist; otherwise the first
        /// match. Returns null when the device list has no matching full-bleed size.
        /// </summary>
        public static string Pick(string sizeLetter, IList<string> mediaList, bool preferLandscape = true)
        {
            if (string.IsNullOrWhiteSpace(sizeLetter) || mediaList == null || mediaList.Count == 0)
                return null;

            string want = sizeLetter.Trim().ToUpperInvariant();
            if (want.StartsWith("A", StringComparison.Ordinal) && want.Length >= 2)
                want = want.Substring(1);

            if (want.Length != 1 || want[0] < '0' || want[0] > '4')
                return null;

            string best = null;
            bool bestIsLandscape = false;

            for (int i = 0; i < mediaList.Count; i++)
            {
                string media = mediaList[i];
                if (media == null) continue;

                Match m = IsoFullBleedPattern.Match(media);
                if (!m.Success) continue;
                if (!string.Equals(m.Groups[1].Value, want, StringComparison.OrdinalIgnoreCase))
                    continue;

                bool landscape = IsLandscapeCanonical(media);
                if (best == null)
                {
                    best = media;
                    bestIsLandscape = landscape;
                    continue;
                }

                if (preferLandscape && landscape && !bestIsLandscape)
                {
                    best = media;
                    bestIsLandscape = true;
                }
                else if (!preferLandscape && !landscape && bestIsLandscape)
                {
                    best = media;
                    bestIsLandscape = false;
                }
            }

            return best;
        }

        /// <summary>True when the media list contains at least one ISO full bleed A0–A4 entry.</summary>
        public static bool HasAny(IList<string> mediaList)
        {
            if (mediaList == null) return false;
            for (int i = 0; i < mediaList.Count; i++)
            {
                string media = mediaList[i];
                if (media != null && IsoFullBleedPattern.IsMatch(media))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Parses "(W.WW x H.HH MM)" from a canonical name; landscape when W &gt; H.
        /// If no dimensions are found, returns false (treat as non-landscape).
        /// </summary>
        public static bool IsLandscapeCanonical(string canonicalMediaName)
        {
            if (string.IsNullOrEmpty(canonicalMediaName)) return false;

            int open = canonicalMediaName.IndexOf('(');
            int close = canonicalMediaName.IndexOf(')');
            if (open < 0 || close <= open) return false;

            string inside = canonicalMediaName.Substring(open + 1, close - open - 1);
            // Canonical names use either "841.00 x 594.00 MM" or "841.00_x_594.00_MM".
            string normalized = inside.Replace('_', ' ');
            int x = normalized.IndexOf(" x ", StringComparison.OrdinalIgnoreCase);
            if (x < 0)
            {
                int bare = normalized.IndexOf('x');
                if (bare < 0) bare = normalized.IndexOf('X');
                if (bare < 0) return false;
                x = bare;
                string leftBare = StripUnit(normalized.Substring(0, x).Trim());
                string rightBare = StripUnit(normalized.Substring(x + 1).Trim());
                return TryCompareLandscape(leftBare, rightBare);
            }

            string left = StripUnit(normalized.Substring(0, x).Trim());
            string right = StripUnit(normalized.Substring(x + 3).Trim());
            return TryCompareLandscape(left, right);
        }

        private static bool TryCompareLandscape(string left, string right)
        {
            if (!double.TryParse(left, NumberStyles.Float, CultureInfo.InvariantCulture, out double w))
                return false;
            if (!double.TryParse(right, NumberStyles.Float, CultureInfo.InvariantCulture, out double h))
                return false;

            return w > h;
        }

        private static string StripUnit(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            int space = value.LastIndexOf(' ');
            if (space > 0) return value.Substring(0, space).Trim();
            // Trailing unit without space: "594.00MM"
            for (int i = value.Length - 1; i >= 0; i--)
            {
                if (char.IsDigit(value[i]) || value[i] == '.')
                    return value.Substring(0, i + 1);
            }

            return value;
        }
    }
}
