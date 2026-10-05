using System;
using System.Collections.Generic;

namespace GvrTools.Civil3D.Export
{
    /// <summary>
    /// Output formats the batch exporter can produce. <see cref="Plot"/> drives AutoCAD's plot
    /// engine with whatever device the user picks, so its file type (PDF, DWF, PNG...) or printer
    /// comes from the device, not from this enum.
    ///
    /// DWG-per-layout is intentionally not modeled yet: unlike Revit (where the model is BIM data
    /// and "export to DWG" means generating one from scratch), a Civil 3D drawing already IS a DWG,
    /// so "exportar a DWG" here would mean something different (WBLOCK-ing a layout into its own
    /// file) and is left for a later iteration.
    /// </summary>
    public enum ExportFormat
    {
        Plot
    }

    /// <summary>Display name and default file extension for each <see cref="ExportFormat"/>.</summary>
    public static class ExportFormatInfo
    {
        private static readonly Dictionary<ExportFormat, string> Extensions = new Dictionary<ExportFormat, string>
        {
            [ExportFormat.Plot] = ".pdf"
        };

        private static readonly Dictionary<ExportFormat, string> Labels = new Dictionary<ExportFormat, string>
        {
            [ExportFormat.Plot] = "Trazado"
        };

        public static string Extension(ExportFormat format) =>
            Extensions.TryGetValue(format, out string extension) ? extension : ".out";

        public static string Label(ExportFormat format) =>
            Labels.TryGetValue(format, out string label) ? label : format.ToString().ToUpperInvariant();

        public static IEnumerable<ExportFormat> All => (ExportFormat[])Enum.GetValues(typeof(ExportFormat));
    }
}
