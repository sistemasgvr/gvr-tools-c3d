using System.Windows;
using System.Windows.Interop;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using GvrTools.Civil3D.Infrastructure;
using GvrTools.Core.Diagnostics;
using GvrTools.Core.History;
using GvrTools.Core.Settings;
using GvrTools.Tools.BatchExport.ViewModels;
using GvrTools.Tools.BatchExport.Views;
using GvrTools.UI.Services;
using AcadApplication = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using SysException = System.Exception;

// Required for AutoCAD to discover instance or static [CommandMethod]s in this satellite DLL
// when it is loaded as a dependency of GvrTools.App (NETLOAD / bundle), not as the primary module.
[assembly: CommandClass(typeof(GvrTools.Tools.BatchExport.BatchExportCommands))]

namespace GvrTools.Tools.BatchExport
{
    /// <summary>
    /// Entry point of the tool: opens the exporter window and returns immediately. Equivalent to
    /// Revit's <c>BatchExportCommand : IExternalCommand</c>, but AutoCAD invokes it by the global
    /// command name declared with <see cref="CommandMethodAttribute"/> rather than by type.
    ///
    /// The command owns the scheduler because the scheduler's dispatcher-based pump needs a WPF
    /// dispatcher to already exist, which a command context guarantees. Both scheduler and window
    /// live until the user closes the window, tracked in a static field so a modeless window is not
    /// collected out from under the user.
    /// </summary>
    public sealed class BatchExportCommands
    {
        public const string CommandName = "GVRBATCHEXPORT";

        private const string DialogTitle = "GVR Tools - Exportación masiva";

        private static BatchExportWindow _openWindow;
        private static CivilJobScheduler _scheduler;

        // Static so AutoCAD registers the command even without relying solely on CommandClass
        // scanning of satellite assemblies pulled in by GvrTools.App.
        [CommandMethod("GVRTOOLS", CommandName, CommandFlags.Session)]
        public static void RunBatchExport()
        {
            Document document = AcadApplication.DocumentManager.MdiActiveDocument;
            Editor editor = document?.Editor;

            if (document == null)
            {
                MessageBox.Show("Abre un dibujo antes de usar esta herramienta.", DialogTitle,
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_openWindow != null)
            {
                if (_openWindow.Document != null && !ReferenceEquals(_openWindow.Document, document))
                {
                    MessageBox.Show(
                        $"Ya hay una exportación abierta para el dibujo \"{_openWindow.DocumentTitle}\". " +
                        "Ciérrala primero si quieres exportar el dibujo activo ahora.",
                        DialogTitle, MessageBoxButton.OK, MessageBoxImage.Information);
                }

                _openWindow.Activate();
                return;
            }

            var log = new RollingFileLog("BatchExport");

            try
            {
                _scheduler = new CivilJobScheduler(log);

                var viewModel = new BatchExportViewModel(
                    document,
                    _scheduler,
                    new WindowsUserDialogs(),
                    new FlatFileSettingsStore(),
                    new SheetExportHistoryStore(),
                    log);

                _openWindow = new BatchExportWindow(viewModel, ReleaseWindow);
                new WindowInteropHelper(_openWindow).Owner = AcadApplication.MainWindow.Handle;
                _openWindow.Show();
            }
            catch (SysException ex)
            {
                log.Error("No se pudo abrir la ventana de exportación masiva.", ex);
                ReleaseWindow();
                editor?.WriteMessage("\nGVR Tools: " + ex.Message);
            }
        }

        private static void ReleaseWindow()
        {
            _openWindow = null;

            _scheduler?.Dispose();
            _scheduler = null;
        }
    }
}
