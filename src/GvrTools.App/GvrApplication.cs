using System;
using System.Collections.Generic;
using System.Windows.Threading;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Windows;
using GvrTools.App.Ribbon;
using GvrTools.Civil3D.Infrastructure;
using GvrTools.Civil3D.Ribbon;
using GvrTools.Core.Diagnostics;
using SysException = System.Exception;

[assembly: ExtensionApplication(typeof(GvrTools.App.GvrApplication))]

namespace GvrTools.App
{
    /// <summary>
    /// The add-in AutoCAD/Civil 3D loads.
    ///
    /// It contains no tool logic at all: it creates the GVR Tools ribbon tab, asks
    /// <see cref="ToolCatalog"/> what tools exist and hands them to <see cref="RibbonBuilder"/>.
    /// Adding, removing or reordering tools therefore never touches this file.
    ///
    /// Licensing is intentionally NOT wired up yet for this Civil 3D MVP (see
    /// GvrTools.Licensing — the project still builds and is referenced, but
    /// <c>LicenseRuntime</c>/entitlement checks are skipped here so every discovered tool is shown
    /// unconditionally). Re-enabling it is a matter of restoring the calls that are commented out
    /// below, the same way GvrTools.Revit's GvrApplication did it.
    /// </summary>
    public class GvrApplication : IExtensionApplication
    {
        public const string TabName = "GVR Tools";

        public void Initialize()
        {
            var log = new RollingFileLog("App");

            try
            {
                log.Info($"Iniciando GVR Tools (compilado para Civil 3D {C3DVersionInfo.CompiledFor}).");

                // Red de seguridad de última instancia, igual que en la versión Revit: cualquier
                // excepción no manejada que llegue al dispatcher del proceso se registra en vez de
                // tumbar AutoCAD con un error irrecuperable.
                Dispatcher.CurrentDispatcher.UnhandledException += (sender, e) =>
                {
                    try
                    {
                        log.Error("Excepción no manejada atrapada por la red de seguridad del dispatcher.", e.Exception);
                    }
                    catch
                    {
                        // el logging nunca debe ser la razón de un segundo fallo.
                    }

                    e.Handled = true;
                };

                AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
                {
                    try
                    {
                        log.Error("Excepción no manejada fuera del dispatcher (el proceso se cerrará).", e.ExceptionObject as SysException);
                    }
                    catch
                    {
                        // el logging nunca debe ser la razón de un segundo fallo.
                    }
                };

                // TODO(licencia): LicenseRuntime.EnsureInitialized() + warmup + entitlements se
                // omiten en este MVP de Civil 3D. Ver GvrTools.Licensing y GvrTools.Revit's
                // GvrApplication.cs (versión anterior) para el flujo completo a restaurar.

                BuildRibbonWhenReady(log);
            }
            catch (SysException ex)
            {
                log.Error("Error al construir la cinta de GVR Tools.", ex);
            }
        }

        public void Terminate()
        {
            // No hay cola de licenciamiento que vaciar en este MVP (ver Initialize).
        }

        /// <summary>
        /// Builds the ribbon now if AutoCAD's ribbon control already exists, or defers until it does.
        ///
        /// <see cref="ComponentManager.Ribbon"/> is null whenever the ribbon palette has not been
        /// realized yet: this happens routinely when the add-in is loaded at startup (the tab strip
        /// is built after our <see cref="Initialize"/> runs) and was also observed on a manual
        /// NETLOAD in certain session states (logged as "ComponentManager.Ribbon es null", 0 tools
        /// added). The previous code gave up in that case, so the tab never appeared. Subscribing to
        /// <see cref="ComponentManager.ItemInitialized"/> — the canonical Autodesk pattern — lets us
        /// build the tab the moment the ribbon becomes available, then unsubscribe.
        /// </summary>
        private void BuildRibbonWhenReady(ILog log)
        {
            if (ComponentManager.Ribbon != null)
            {
                BuildRibbon(log);
                return;
            }

            log.Info("La cinta de AutoCAD aún no está lista; se construirá cuando se inicialice.");

            void OnItemInitialized(object sender, RibbonItemEventArgs e)
            {
                if (ComponentManager.Ribbon == null) return;

                ComponentManager.ItemInitialized -= OnItemInitialized;
                BuildRibbon(log);
            }

            ComponentManager.ItemInitialized += OnItemInitialized;
        }

        /// <summary>
        /// Discovers the tools and turns them into ribbon buttons. Runs either inline (ribbon already
        /// present) or later from the <see cref="ComponentManager.ItemInitialized"/> handler, so it
        /// owns its own try/catch instead of relying on <see cref="Initialize"/>'s.
        /// </summary>
        private void BuildRibbon(ILog log)
        {
            try
            {
                IReadOnlyList<ICivilTool> discovered = ToolCatalog.Discover(log);

                if (discovered.Count == 0)
                {
                    log.Warn("No se encontró ninguna herramienta para agregar a la cinta.");
                    return;
                }

                var builder = new RibbonBuilder(TabName, log);
                int added = 0;

                foreach (ICivilTool tool in discovered)
                {
                    if (builder.Add(tool)) added++;
                }

                log.Info($"Cinta lista: {added} de {discovered.Count} herramienta(s) agregadas.");
            }
            catch (SysException ex)
            {
                log.Error("Error al construir la cinta de GVR Tools.", ex);
            }
        }
    }
}
