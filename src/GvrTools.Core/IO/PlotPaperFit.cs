using System;

namespace GvrTools.Core.IO
{
    /// <summary>
    /// Detects the classic batch-plot trap: forcing a paper smaller than the sheet a layout was
    /// drawn for (an A1 layout on ISO A4) at a fixed scale, which plots only the middle of the
    /// drawing. Pure math (millimetres), so the warning is testable without AutoCAD.
    /// </summary>
    public static class PlotPaperFit
    {
        /// <summary>ISO 216 A-series sheet in millimetres, landscape (width ≥ height).</summary>
        public static bool TryGetIsoSize(string sizeLetter, out double width, out double height)
        {
            width = 0;
            height = 0;

            switch ((sizeLetter ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "A0": width = 1189; height = 841; return true;
                case "A1": width = 841; height = 594; return true;
                case "A2": width = 594; height = 420; return true;
                case "A3": width = 420; height = 297; return true;
                case "A4": width = 297; height = 210; return true;
                default: return false;
            }
        }

        /// <summary>
        /// True when a sheet of <paramref name="sheetWidth"/> x <paramref name="sheetHeight"/> mm,
        /// plotted at <paramref name="scaleRatio"/> (paper units per drawing unit, 1 for 1:1, 0.5 for
        /// 1:2), does not fit the target paper in its best orientation.
        /// </summary>
        public static bool WouldClip(
            double sheetWidth,
            double sheetHeight,
            double targetWidth,
            double targetHeight,
            double scaleRatio,
            double toleranceMm = 1.0)
        {
            if (sheetWidth <= 0 || sheetHeight <= 0 || targetWidth <= 0 || targetHeight <= 0 || scaleRatio <= 0)
                return false;

            double sheetLong = Math.Max(sheetWidth, sheetHeight) * scaleRatio;
            double sheetShort = Math.Min(sheetWidth, sheetHeight) * scaleRatio;
            double targetLong = Math.Max(targetWidth, targetHeight);
            double targetShort = Math.Min(targetWidth, targetHeight);

            return sheetLong > targetLong + toleranceMm || sheetShort > targetShort + toleranceMm;
        }
    }
}
