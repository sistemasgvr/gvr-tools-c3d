using System;
using System.Collections.Generic;
using System.Globalization;

namespace GvrTools.Core.IO
{
    /// <summary>
    /// One entry of a plot scale list: <see cref="PaperUnits"/> on paper equal
    /// <see cref="DrawingUnits"/> in the drawing ("1:100" = 1 mm of paper per 100 units).
    /// Pure data, so the list AutoCAD shows (the drawing's SCALELISTEDIT entries) and the fallback
    /// list below can be compared and tested without an AutoCAD host.
    /// </summary>
    public sealed class PlotScale
    {
        private const double Tolerance = 1e-9;

        public PlotScale(string name, double paperUnits, double drawingUnits)
        {
            PaperUnits = paperUnits;
            DrawingUnits = drawingUnits;
            Name = string.IsNullOrWhiteSpace(name) ? FormatName(paperUnits, drawingUnits) : name.Trim();
        }

        public string Name { get; }

        public double PaperUnits { get; }

        public double DrawingUnits { get; }

        public bool IsValid => PaperUnits > 0 && DrawingUnits > 0;

        /// <summary>True when both scales describe the same ratio (1:100 == 10:1000).</summary>
        public bool HasSameRatio(double paperUnits, double drawingUnits)
        {
            if (!IsValid || paperUnits <= 0 || drawingUnits <= 0) return false;

            double mine = PaperUnits / DrawingUnits;
            double other = paperUnits / drawingUnits;
            return Math.Abs(mine - other) <= Tolerance * Math.Max(mine, other);
        }

        /// <summary>"1:100", "2:1", "1:2.5" — the way AutoCAD's scale list names custom entries.</summary>
        public static string FormatName(double paperUnits, double drawingUnits) =>
            FormatNumber(paperUnits) + ":" + FormatNumber(drawingUnits);

        private static string FormatNumber(double value) =>
            value.ToString("0.####", CultureInfo.InvariantCulture);

        public override string ToString() => Name;
    }

    /// <summary>Scale lists offered by the plot panel.</summary>
    public static class PlotScaleList
    {
        /// <summary>
        /// Metric list used when the drawing has no scale list of its own (or none can be read,
        /// e.g. the folder exporter with no drawing open). Mirrors the default SCALELISTEDIT list of
        /// a metric Civil 3D template.
        /// </summary>
        public static IReadOnlyList<PlotScale> Default { get; } = new[]
        {
            new PlotScale("1:1", 1, 1),
            new PlotScale("1:2", 1, 2),
            new PlotScale("1:5", 1, 5),
            new PlotScale("1:10", 1, 10),
            new PlotScale("1:20", 1, 20),
            new PlotScale("1:25", 1, 25),
            new PlotScale("1:50", 1, 50),
            new PlotScale("1:75", 1, 75),
            new PlotScale("1:100", 1, 100),
            new PlotScale("1:125", 1, 125),
            new PlotScale("1:200", 1, 200),
            new PlotScale("1:250", 1, 250),
            new PlotScale("1:500", 1, 500),
            new PlotScale("1:750", 1, 750),
            new PlotScale("1:1000", 1, 1000),
            new PlotScale("1:1250", 1, 1250),
            new PlotScale("1:2000", 1, 2000),
            new PlotScale("1:2500", 1, 2500),
            new PlotScale("1:5000", 1, 5000),
            new PlotScale("1:10000", 1, 10000),
            new PlotScale("2:1", 2, 1),
            new PlotScale("5:1", 5, 1),
            new PlotScale("10:1", 10, 1),
            new PlotScale("100:1", 100, 1)
        };

        /// <summary>First entry of <paramref name="scales"/> with the same ratio, or null.</summary>
        public static PlotScale Find(IEnumerable<PlotScale> scales, double paperUnits, double drawingUnits)
        {
            if (scales == null) return null;

            foreach (PlotScale scale in scales)
            {
                if (scale != null && scale.HasSameRatio(paperUnits, drawingUnits))
                    return scale;
            }

            return null;
        }

        /// <summary>
        /// Drops invalid entries and duplicate ratios, keeping the input order, and falls back to
        /// <see cref="Default"/> when nothing usable is left. Among duplicates the cleanest name wins:
        /// inserting blocks or xrefs leaves copies such as "1:1_1" or "1:100_XREF" in the scale list,
        /// and the Plot dialog user expects to see "1:1".
        /// </summary>
        public static IReadOnlyList<PlotScale> Normalize(IEnumerable<PlotScale> scales)
        {
            var result = new List<PlotScale>();

            if (scales != null)
            {
                foreach (PlotScale scale in scales)
                {
                    if (scale == null || !scale.IsValid) continue;

                    PlotScale existing = Find(result, scale.PaperUnits, scale.DrawingUnits);
                    if (existing == null)
                    {
                        result.Add(scale);
                    }
                    else if (NameQuality(scale) > NameQuality(existing))
                    {
                        result[result.IndexOf(existing)] = scale;
                    }
                }
            }

            return result.Count > 0 ? result : Default;
        }

        /// <summary>2 = canonical "1:100", 1 = other plain name, 0 = copy suffix ("_1", "_XREF").</summary>
        private static int NameQuality(PlotScale scale)
        {
            if (string.Equals(scale.Name, PlotScale.FormatName(scale.PaperUnits, scale.DrawingUnits), StringComparison.Ordinal))
                return 2;

            return scale.Name.IndexOf('_') >= 0 ? 0 : 1;
        }
    }
}
