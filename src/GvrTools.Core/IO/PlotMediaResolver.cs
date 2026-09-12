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
