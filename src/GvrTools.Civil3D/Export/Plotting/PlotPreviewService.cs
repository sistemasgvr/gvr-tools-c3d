using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.PlottingServices;
using GvrTools.Civil3D.Layouts;
using GvrTools.Civil3D.Model;
using GvrTools.Core.Diagnostics;

namespace GvrTools.Civil3D.Export.Plotting
{
    /// <summary>
    /// The interactive bits of the Plot dialog the batch window offers: "Preview..." and the
    /// "Window&lt;" pick. Both make the layout current (AutoCAD previews and picks only on the
    /// current layout) and put the user's previous layout back afterwards.
    /// </summary>
    public static class PlotPreviewService
    {
        /// <summary>
        /// Shows AutoCAD's plot preview of <paramref name="layout"/> plotted exactly as the batch would
        /// (same <see cref="PlotInfoBuilder"/>). Blocks until the user closes the preview.
        /// Returns null when the preview ran, otherwise a user-facing reason why it could not.
        /// </summary>
        public static string ShowPreview(Document document, LayoutSnapshot layout, PlotExportSettings settings, ILog log)
        {
            log = log ?? NullLog.Instance;

            if (document == null || layout == null)
                return "Selecciona una presentación para la vista preliminar.";

            if (PlotFactory.ProcessPlotState != ProcessPlotState.NotPlotting)
                return "Ya hay un trazado en curso en AutoCAD. Espera a que termine.";

            string originalLayout = SafeCurrentLayout();

            try
            {
                using (document.LockDocument())
                using (var tr = document.Database.TransactionManager.StartTransaction())
                {
                    Layout liveLayout = LayoutRepository.Resolve(document.Database, tr, layout);
                    if (liveLayout == null)
                        return "La presentación ya no existe en el dibujo.";

                    MakeCurrent(liveLayout.LayoutName);

                    using (var plotInfo = new PlotInfo())
                    {
                        try
                        {
                            new PlotInfoBuilder(settings, log).Build(plotInfo, liveLayout);
                        }
                        catch (PlotDeviceSetupException ex)
                        {
                            return ex.Message;
                        }

                        using (PlotEngine engine = PlotFactory.CreatePreviewEngine(0))
                        {
                            engine.BeginPlot(null, null);
                            engine.BeginDocument(plotInfo, document.Name, null, 1, false, null);
                            engine.BeginPage(new PlotPageInfo(), plotInfo, true, null);
                            engine.BeginGenerateGraphics(null);
                            engine.EndGenerateGraphics(null);
                            engine.EndPage(new PreviewEndPlotInfo());
                            engine.EndDocument(null);
                            engine.EndPlot(null);
                        }
                    }

                    tr.Commit();
                }

                return null;
            }
            catch (Exception ex)
            {
                log.Error("No se pudo mostrar la vista preliminar.", ex);
                return "No se pudo mostrar la vista preliminar: " + ex.Message;
            }
            finally
            {
                Restore(document, originalLayout, log);
            }
        }

        /// <summary>
        /// Asks the user for the two corners of a plot window in the paper space of
        /// <paramref name="layoutName"/>, hiding <paramref name="owner"/> meanwhile (as the Plot
        /// dialog does). Returns false when the user cancels.
        /// </summary>
        public static bool TryPickWindow(Document document, string layoutName, System.Windows.Window owner, ILog log, out Extents2d window)
        {
            window = default(Extents2d);
            log = log ?? NullLog.Instance;
            if (document == null) return false;

            Editor editor = document.Editor;

            using (document.LockDocument())
            {
                if (!string.IsNullOrEmpty(layoutName)) MakeCurrent(layoutName);

                // Dentro de una ventana gráfica activa los puntos serían de espacio modelo.
                try { editor.SwitchToPaperSpace(); }
                catch (Exception ex) { log.Warn("No se pudo pasar a espacio papel: " + ex.Message); }
            }

            using (EditorUserInteraction interaction = editor.StartUserInteraction(owner))
            {
                PromptPointResult first = editor.GetPoint(
                    new PromptPointOptions("\nPrimera esquina de la ventana de trazado: "));
                if (first.Status != PromptStatus.OK) return false;

                PromptPointResult second = editor.GetCorner(
                    new PromptCornerOptions("\nEsquina opuesta: ", first.Value));
                if (second.Status != PromptStatus.OK) return false;

                interaction.End();

                Matrix3d ucs = editor.CurrentUserCoordinateSystem;
                Point3d a = first.Value.TransformBy(ucs);
                Point3d b = second.Value.TransformBy(ucs);

                window = new Extents2d(
                    Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
                    Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
                return true;
            }
        }

        private static void MakeCurrent(string layoutName)
        {
            LayoutManager lm = LayoutManager.Current;
            if (!string.Equals(lm.CurrentLayout, layoutName, StringComparison.Ordinal))
                lm.CurrentLayout = layoutName;
        }

        private static string SafeCurrentLayout()
        {
            try { return LayoutManager.Current.CurrentLayout; }
            catch (Exception) { return null; }
        }

        private static void Restore(Document document, string layoutName, ILog log)
        {
            if (string.IsNullOrEmpty(layoutName)) return;

            try
            {
                using (document.LockDocument())
                    MakeCurrent(layoutName);
            }
            catch (Exception ex)
            {
                log.Warn("No se pudo restaurar la presentación activa: " + ex.Message);
            }
        }
    }
}
