using System;
using System.Collections.Generic;

namespace GvrTools.Core.IO
{
    /// <summary>
    /// Picks a fallback AutoCAD canonical media name when the layout's paper size is not offered by
    /// the chosen plot device. Pure string logic (no AutoCAD dependency).
    /// </summary>
    public static class PlotMediaResolver
    {
        /// <summary>
        /// Returns <paramref name="desiredMedia"/> when it appears in <paramref name="mediaList"/>
        /// (ordinal-ignore-case); otherwise null.
        /// </summary>
        public static string FindExact(string desiredMedia, IList<string> mediaList)
        {
            if (mediaList == null || mediaList.Count == 0 || string.IsNullOrWhiteSpace(desiredMedia))
                return null;

            for (int i = 0; i < mediaList.Count; i++)
            {
                string media = mediaList[i];
                if (media != null &&
                    string.Equals(media, desiredMedia, StringComparison.OrdinalIgnoreCase))
                {
                    return media;
                }
            }

            return null;
        }

        /// <summary>
        /// Paper for a layout on a device that may not have the layout's exact media, in order:
        /// the same media; one with the same physical size (another device's name for A1, either
        /// orientation); the device's own default (sensible for printers and pixel devices);
        /// then the common-size heuristics of <see cref="Resolve(string,IList{string})"/>.
        /// </summary>
        public static string Resolve(string desiredMedia, IList<string> mediaList, string deviceDefaultMedia)
        {
            if (mediaList == null || mediaList.Count == 0) return null;

            return FindExact(desiredMedia, mediaList)
                ?? FindSameSize(desiredMedia, mediaList)
                ?? FindExact(deviceDefaultMedia, mediaList)
                ?? Resolve(desiredMedia, mediaList);
        }

        /// <summary>
        /// A media of the same physical size as <paramref name="desiredMedia"/> (± tolerance, in
        /// millimetres), preferring the same orientation. Null when sizes are unknown or none match.
        /// </summary>
        public static string FindSameSize(string desiredMedia, IList<string> mediaList, double toleranceMm = 1.0)
        {
            if (mediaList == null || !PlotMediaNames.TryGetSizeInMillimeters(desiredMedia, out double width, out double height))
                return null;

            string rotated = null;
            foreach (string media in mediaList)
            {
                if (!PlotMediaNames.TryGetSizeInMillimeters(media, out double w, out double h)) continue;

                if (Math.Abs(w - width) <= toleranceMm && Math.Abs(h - height) <= toleranceMm)
                    return media;

                if (rotated == null && Math.Abs(w - height) <= toleranceMm && Math.Abs(h - width) <= toleranceMm)
                    rotated = media;
            }

            return rotated;
        }

        /// <summary>
        /// Returns <paramref name="desiredMedia"/> when it appears in <paramref name="mediaList"/>
        /// (ordinal-ignore-case); otherwise the first preferred common size found, else the first
        /// entry. Returns null when the list is empty.
        /// </summary>
        public static string Resolve(string desiredMedia, IList<string> mediaList)
        {
            if (mediaList == null || mediaList.Count == 0) return null;

            if (!string.IsNullOrWhiteSpace(desiredMedia))
            {
                for (int i = 0; i < mediaList.Count; i++)
                {
                    string media = mediaList[i];
                    if (media != null &&
                        string.Equals(media, desiredMedia, StringComparison.OrdinalIgnoreCase))
                    {
                        return media;
                    }
                }
            }

            string[] preferred =
            {
                "ISO_full_bleed_A1_",
                "ISO_full_bleed_A0_",
                "ISO_full_bleed_A2_",
                "ISO_full_bleed_A3_",
                "ISO_full_bleed_A4_",
                "ISO_A1_",
                "ISO_A0_",
                "ISO_A4_",
                "ANSI_A_",
                "A4",
                "Letter"
            };

            foreach (string want in preferred)
            {
                for (int i = 0; i < mediaList.Count; i++)
                {
                    string media = mediaList[i];
                    if (media != null &&
                        media.IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return media;
                    }
                }
            }

            return mediaList[0];
        }
    }
}
