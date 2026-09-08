using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using GvrTools.Core.Diagnostics;
using GvrTools.Licensing.Http.Dto;

namespace GvrTools.Licensing.Activation
{
    /// <summary>Helpers para abrir las ventanas de licencia desde comandos Revit (owner HWND).</summary>
    public static class LicenseUi
    {
        /// <summary>
        /// Opcional: lo registra el host Revit para cerrar/reiniciar la aplicación tras activar.
        /// </summary>
        public static Action RequestApplicationClose { get; set; }

        /// <summary>
        /// Ventana unificada Cuenta / Cambiar plan: resumen + soporte + pegar key + desactivar
        /// (UI_FREEMIUM_PLAN.md §3.2). Único punto de entrada para activar/reactivar/desactivar --
        /// antes existía también ActivateLicenseWindow (solo campos, sin resumen de plan) para el
        /// arranque/kick/gate de herramientas; auditoría del sistema encontró que duplicaba casi
        /// toda esta lógica con UX distinta según el punto de entrada, así que se retiró.
        /// <paramref name="initialMessage"/> es opcional: si no se pasa, se usa
        /// <see cref="LicenseClient.ReactivationReason"/> del cliente (ya lo lee
        /// AccountLicenseViewModel.PlanSummary), que es lo que la mayoría de los llamadores ya
        /// tienen puesto antes de llegar aquí (MarkNeedsReactivation corre primero).
        /// </summary>
        public static void ShowChangePlan(LicenseClient client = null, System.IntPtr ownerHwnd = default)
        {
            var vm = new AccountLicenseViewModel(client ?? LicenseRuntime.Client);
            var window = new AccountLicenseWindow(vm);
            AttachOwner(window, ownerHwnd);
            bool? result = window.ShowDialog();
            if (result == true || window.NeedsRestart)
                PromptRestartAfterActivation(window.RestartReason);
        }

        /// <summary>Alias de <see cref="ShowChangePlan"/> -- mismo diálogo, distinto punto de entrada semántico (ribbon "Cuenta / Licencia").</summary>
        public static void ShowAccount(LicenseClient client = null, System.IntPtr ownerHwnd = default)
            => ShowChangePlan(client, ownerHwnd);

        /// <summary>
        /// Aviso al usuario y cierre diferido de Revit (cuando el dispatcher esté idle).
        /// Debe llamarse solo cuando ya no hay ShowDialog de licencia abiertos.
        /// </summary>
        public static void PromptRestartAfterActivation(string reason = null)
        {
            MessageBox.Show(
                (string.IsNullOrWhiteSpace(reason) ? "Licencia activada correctamente." : reason.Trim()) + "\n\n" +
                "Al pulsar Aceptar, Revit se cerrará y se volverá a abrir solo para cargar todas las herramientas de tu plan.\n\n" +
                "Si tenías un proyecto guardado abierto, se reabrirá automáticamente.\n" +
                "Si Revit pregunta si guardar, confirma o descarta según corresponda.",
                "GVR Tools · Reiniciar Revit",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            ScheduleApplicationClose();
        }

        /// <summary>
        /// Encola el cierre cuando WPF/Revit ya no están dentro de un diálogo modal.
        /// </summary>
        public static void ScheduleApplicationClose()
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted)
            {
                InvokeRequestApplicationClose();
                return;
            }

            dispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle,
                new Action(InvokeRequestApplicationClose));
        }

        /// <summary>
        /// Este delegado corre diferido -- vía Dispatcher.BeginInvoke o, más adelante, el evento
        /// Idling de Revit -- fuera de la pila del comando que lo originó. No hay ningún
        /// AppDomain/Dispatcher.UnhandledException registrado en todo el add-in, así que si algo
        /// dentro (resolver Revit.exe, escribir el script de reinicio, lanzar el proceso) lanzara
        /// sin atrapar, no hay nada que lo detenga: una excepción no controlada aquí tumba Revit
        /// entero con "error irrecuperable" en vez de solo fallar el reinicio.
        /// </summary>
        private static void InvokeRequestApplicationClose()
        {
            try
            {
                RequestApplicationClose?.Invoke();
            }
            catch (Exception ex)
            {
                try
                {
                    new RollingFileLog("App").Error("No se pudo reiniciar Revit tras activar la licencia.", ex);
                }
                catch
                {
                    // el logging nunca debe ser la razón de un segundo fallo.
                }
            }
        }

        /// <summary>
        /// Aviso no modal: el usuario puede seguir trabajando en Revit y descargar cuando quiera.
        /// </summary>
        public static void ShowUpdateAvailable(
            UpdateCheckResponse update,
            string currentVersion,
            LicenseClient client = null,
            System.IntPtr ownerHwnd = default)
        {
            if (update == null || !update.UpdateAvailable) return;

            var vm = new UpdateAvailableViewModel(client ?? LicenseRuntime.Client, update, currentVersion);
            var window = new UpdateAvailableWindow(vm);
            AttachOwner(window, ownerHwnd);
            window.Show();
        }

        private static void AttachOwner(Window window, System.IntPtr ownerHwnd)
        {
            if (ownerHwnd != System.IntPtr.Zero)
                new WindowInteropHelper(window).Owner = ownerHwnd;
        }
    }
}
