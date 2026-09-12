using System.Windows;
using System.Windows.Interop;
using Autodesk.AutoCAD.Runtime;
using GvrTools.Civil3D.Infrastructure;
using GvrTools.Core.Diagnostics;
using GvrTools.Core.Settings;
using GvrTools.Tools.BatchExport.ViewModels;
using GvrTools.Tools.BatchExport.Views;
using GvrTools.UI.Services;
using AcadApplication = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using SysException = System.Exception;

// Required for AutoCAD to discover instance or static [CommandMethod]s in this satellite DLL
// when it is loaded as a dependency of GvrTools.App (NETLOAD / bundle), not as the primary module.
[assembly: CommandClass(typeof(GvrTools.Tools.BatchExport.FolderBatchExportCommands))]

namespace GvrTools.Tools.BatchExport
{
    /// <summary>
    /// Entry point of the multi-drawing exporter: opens its window and returns immediately. Unlike
    /// <see cref="BatchExportCommands"/> this tool does not need an active document up front — it
    /// opens each source drawing itself — but it still needs one open AutoCAD/Civil 3D session to
    /// run in, and <see cref="CivilJobScheduler"/> needs a WPF dispatcher that a command context
    /// guarantees exists.
    /// </summary>
    public sealed class FolderBatchExportCommands
    {
        public const string CommandName = "GVRFOLDERBATCHEXPORT";

        private const string DialogTitle = "GVR Tools - Exportación masiva de dibujos";

        private static FolderBatchExportWindow _openWindow;
        private static CivilJobScheduler _scheduler;

        [CommandMethod("GVRTOOLS", CommandName, CommandFlags.Session)]
        public static void RunFolderBatchExport()
        {
            if (_openWindow != null)
            {
                _openWindow.Activate();
                return;
            }

            var log = new RollingFileLog("BatchExport");

            try
            {
                _scheduler = new CivilJobScheduler(log);

                var viewModel = new FolderBatchExportViewModel(
                    _scheduler,
                    new WindowsUserDialogs(),
                    new FlatFileSettingsStore(),
                    log);

                _openWindow = new FolderBatchExportWindow(viewModel, ReleaseWindow);
                new WindowInteropHelper(_openWindow).Owner = AcadApplication.MainWindow.Handle;
                _openWindow.Show();
            }
            catch (SysException ex)
            {
                log.Error("No se pudo abrir la ventana de exportación masiva de dibujos.", ex);
                ReleaseWindow();
                MessageBox.Show("GVR Tools: " + ex.Message, DialogTitle, MessageBoxButton.OK, MessageBoxImage.Error);
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
