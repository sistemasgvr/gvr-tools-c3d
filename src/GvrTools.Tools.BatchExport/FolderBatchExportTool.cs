using System;
using System.Windows;
using System.Windows.Media;
using GvrTools.Civil3D.Ribbon;
using GvrTools.UI.Icons;

namespace GvrTools.Tools.BatchExport
{
    /// <summary>Ribbon registration for the multi-drawing (folder) batch exporter.</summary>
    public sealed class FolderBatchExportTool : CivilToolBase
    {
        public override string Id => "GvrFolderBatchExport";

        public override string Title => "Exportar" + Environment.NewLine + "carpeta de DWG";

        public override string PanelName => "Exportación";

        public override int SortOrder => 20;

        public override string CommandName => FolderBatchExportCommands.CommandName;

        public override string RequiredFeature => null; // Licenciamiento pausado en este MVP.

        public override string Tooltip =>
            "Exporta a PDF todas las presentaciones de varios dibujos (.dwg) de una carpeta, sin abrirlos uno por uno.";

        public override string LongDescription =>
            "Abre cada dibujo brevemente en esta sesión, exporta sus presentaciones a PDF con el mismo motor " +
            "nativo de trazado, y lo cierra sin guardar antes de pasar al siguiente.";

        public override ImageSource CreateIcon() => VectorIcon.Compose(
            VectorIcon.FilledRectangle(new Rect(3, 5, 16, 22), Colors.White, Color.FromRgb(0x45, 0x5A, 0x64), 1.5, 2),
            VectorIcon.FilledRectangle(new Rect(9, 3, 16, 22), Colors.White, Color.FromRgb(0x45, 0x5A, 0x64), 1.5, 2),
            VectorIcon.Rectangle(new Rect(12, 18, 10, 5), Color.FromRgb(0xD3, 0x2F, 0x2F)),
            VectorIcon.Polygon(Color.FromRgb(0x15, 0x65, 0xC0), new Point(14, 11), new Point(24, 11), new Point(19, 16)));
    }
}
