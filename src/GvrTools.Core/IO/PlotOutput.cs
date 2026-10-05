namespace GvrTools.Core.IO
{
    /// <summary>What a plot produces, decided by the device's file extension and plot-to-file mode.</summary>
    public enum PlotOutputKind
    {
        /// <summary>Sent to a physical/system printer; no file is written.</summary>
        Printer,

        Pdf,

        /// <summary>DWF / DWFx.</summary>
        Dwf,

        /// <summary>Raster image (PublishToWeb PNG/JPG, TIFF, BMP...).</summary>
        Image,

        /// <summary>Any other plot file (.plt from a printer driver, HPGL...).</summary>
        PlotFile
    }

    /// <summary>
    /// Classifies plot devices by what they write. Pure string logic (no AutoCAD dependency): the
    /// caller passes <c>PlotConfig.DefaultFileExtension</c> and whether the run plots to a file.
    /// </summary>
    public static class PlotOutput
    {
        public static PlotOutputKind Classify(string defaultExtension, bool plotsToFile)
        {
            if (!plotsToFile) return PlotOutputKind.Printer;

            switch (NormalizeExtension(defaultExtension).ToLowerInvariant())
            {
                case ".pdf":
                    return PlotOutputKind.Pdf;
                case ".dwf":
                case ".dwfx":
                    return PlotOutputKind.Dwf;
                case ".png":
                case ".jpg":
                case ".jpeg":
                case ".bmp":
                case ".tif":
                case ".tiff":
                case ".gif":
                case ".tga":
                case ".pcx":
                    return PlotOutputKind.Image;
                default:
                    return PlotOutputKind.PlotFile;
            }
        }

        /// <summary>Formats that hold several sheets in one file (AutoCAD's multi-sheet plot).</summary>
        public static bool SupportsMultiSheet(PlotOutputKind kind) =>
            kind == PlotOutputKind.Pdf || kind == PlotOutputKind.Dwf;

        /// <summary>".pdf" from "pdf", ".pdf" or " PDF "; ".plt" when the device reports nothing.</summary>
        public static string NormalizeExtension(string extension)
        {
            string trimmed = (extension ?? string.Empty).Trim();
            if (trimmed.Length == 0) return ".plt";

            return trimmed[0] == '.' ? trimmed : "." + trimmed;
        }

        /// <summary>Prefix of the per-run output folder: "PDF", "DWF", "PNG"...</summary>
        public static string FolderPrefix(PlotOutputKind kind, string extension)
        {
            switch (kind)
            {
                case PlotOutputKind.Pdf: return "PDF";
                case PlotOutputKind.Dwf: return "DWF";
                case PlotOutputKind.Image:
                    return NormalizeExtension(extension).TrimStart('.').ToUpperInvariant();
                default:
                    return "Trazado";
            }
        }

        /// <summary>User-facing name of the output, for status lines ("PDF", "impresora").</summary>
        public static string Describe(PlotOutputKind kind, string extension)
        {
            switch (kind)
            {
                case PlotOutputKind.Printer: return "impresora";
                case PlotOutputKind.Pdf: return "PDF";
                case PlotOutputKind.Dwf: return "DWF";
                case PlotOutputKind.Image:
                    return "imagen " + NormalizeExtension(extension).TrimStart('.').ToUpperInvariant();
                default:
                    return "archivo " + NormalizeExtension(extension);
            }
        }

        public static bool IsFileOutput(PlotOutputKind kind) => kind != PlotOutputKind.Printer;

        /// <summary>
        /// Raster devices measure paper in pixels; everything else in millimetres or inches.
        /// </summary>
        public static bool UsesPixels(PlotOutputKind kind) => kind == PlotOutputKind.Image;
    }
}
