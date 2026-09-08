using System;
using System.Windows;
using System.Windows.Media;
using GvrTools.Civil3D.Ribbon;
using GvrTools.UI.Icons;

namespace GvrTools.Tools.BatchExport
{
    /// <summary>
    /// Ribbon registration for the batch exporter. This class is all the host application needs to
    /// know about the tool: it is discovered by assembly scan and wired to
    /// <see cref="BatchExportCommands.RunBatchExport"/> by <see cref="CommandName"/>.
    /// </summary>
    public sealed class BatchExportTool : CivilToolBase
    {
        public override string Id => "GvrBatchExport";

        public override string Title => "Exportar" + Environment.NewLine + "presentaciones";

        public override string PanelName => "Exportación";

        public override int SortOrder => 10;

        public override string CommandName => BatchExportCommands.CommandName;

        public override string RequiredFeature => null; // Licenciamiento pausado en este MVP.

        public override string Tooltip =>
            "Exporta presentaciones (layouts) a PDF de forma masiva, un archivo por presentación.";

        public override string LongDescription =>
            "Usa el motor de trazado nativo de AutoCAD (DWG To PDF.pc3): no abre ventanas, " +
            "no ocupa el teclado y el equipo queda libre mientras se exporta.";

        public override ImageSource CreateIcon() => VectorIcon.Compose(
            VectorIcon.FilledRectangle(new Rect(6, 3, 20, 26), Colors.White, Color.FromRgb(0x45, 0x5A, 0x64), 1.5, 2),
            VectorIcon.Polygon(Color.FromRgb(0xCF, 0xD8, 0xDC), new Point(18, 3), new Point(26, 3), new Point(26, 11)),
            VectorIcon.Rectangle(new Rect(6, 20, 20, 9), Color.FromRgb(0xD3, 0x2F, 0x2F)),
            VectorIcon.Rectangle(new Rect(14, 6, 4, 7), Color.FromRgb(0x15, 0x65, 0xC0)),
            VectorIcon.Polygon(Color.FromRgb(0x15, 0x65, 0xC0), new Point(9, 13), new Point(23, 13), new Point(16, 19)));
    }
}
