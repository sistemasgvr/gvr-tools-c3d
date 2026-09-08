using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using GvrTools.Licensing.Entitlements;
using GvrTools.Licensing.Storage;
using GvrTools.Licensing.Validation;
using GvrTools.UI.Mvvm;

namespace GvrTools.Licensing.Activation
{
    /// <summary>
    /// Ventana unificada "Cambiar plan" / Cuenta: resumen del plan, soporte, pegar key y
    /// desactivar PC (UI_FREEMIUM_PLAN.md §3.2).
    /// </summary>
    public sealed class AccountLicenseViewModel : ObservableObject
    {
        private readonly LicenseClient _client;
        private readonly FileActivationProfileStore _profileStore;
        private CancellationTokenSource _cts;

        public AccountLicenseViewModel(LicenseClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _profileStore = new FileActivationProfileStore();

            var profile = _profileStore.Load();
            _fullName = profile.FullName;
            _email = profile.Email;

            ActivateCommand = new RelayCommand(async () => await ActivateAsync(), () => !IsBusy && !string.IsNullOrWhiteSpace(LicenseKey));
            DeactivateCommand = new RelayCommand(async () => await DeactivateAsync(), () => IsLicensed && !IsBusy);
            QuoteCommand = new RelayCommand(OpenQuotePage);
            CloseCommand = new RelayCommand(() => RequestClose?.Invoke(false));
            Refresh();
        }

        /// <summary>true = el plan cambió (activó o desactivó) y el host debe reiniciar Revit.</summary>
        public event Action<bool> RequestClose;

        /// <summary>
        /// Texto para el MessageBox de reinicio -- distinto según se haya activado o desactivado,
        /// para no mostrar "Licencia activada correctamente" tras un "Desactivar este PC".
        /// </summary>
        public string RestartReason { get; private set; }

        public void Refresh()
        {
            Raise(
                nameof(IsLicensed),
                nameof(PlanSummary),
                nameof(GraceSummary),
                nameof(QuotaSummary),
                nameof(StatusHeadline),
                nameof(SupportHint),
                nameof(ShowDeactivate));
            DeactivateCommand.RaiseCanExecuteChanged();
            ActivateCommand.RaiseCanExecuteChanged();
        }

        public bool IsLicensed => _client.IsLicensed;

        public bool ShowDeactivate => IsLicensed;

        public string StatusHeadline =>
            IsLicensed
                ? "Tu plan actual"
                : (_client.NeedsReactivation
                    ? "Sesión expirada — reactiva"
                    : "Cambiar plan / Activar licencia");

        public string PlanSummary =>
            IsLicensed
                ? ("Plan: " + (_client.PlanDisplayName ?? "—"))
                : (!string.IsNullOrWhiteSpace(_client.ReactivationReason)
                    ? _client.ReactivationReason
                    : "Activa una clave de licencia para desbloquear más formatos y cuota.");

        public string GraceSummary
        {
            get
            {
                if (!IsLicensed || _client.OfflineUntilUtc == null) return string.Empty;
                return "Válida sin conexión hasta: " +
                       _client.OfflineUntilUtc.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture);
            }
        }

        public string QuotaSummary
        {
            get
            {
                if (!IsLicensed) return string.Empty;
                string usage = QuotaDisplay.FormatSheetsUsage(_client.Entitlements);
                return string.IsNullOrEmpty(usage) ? string.Empty : "Cuota de láminas: " + usage;
            }
        }

        /// <summary>
        /// El correo de soporte es un beneficio de los planes de pago -- en el plan free solo se
        /// muestra la invitación a pegar una clave para subir de plan, sin el contacto de soporte.
        /// </summary>
        public string SupportHint
        {
            get
            {
                const string upgradeHint = "Pega aquí la clave que te enviemos para subir de plan.";
                bool isFree = string.Equals(_client.PlanCode, "free", StringComparison.OrdinalIgnoreCase);
                if (isFree) return upgradeHint;

                return "Soporte: " + (_client.SupportEmailHint ?? "contacta a tu administrador GVR") + ". " + upgradeHint;
            }
        }

        private string _licenseKey;
        public string LicenseKey
        {
            get => _licenseKey;
            set
            {
                if (Set(ref _licenseKey, value))
                    ActivateCommand.RaiseCanExecuteChanged();
            }
        }

        private string _fullName;
        public string FullName
        {
            get => _fullName;
            set => Set(ref _fullName, value);
        }

        private string _email;
        public string Email
        {
            get => _email;
            set => Set(ref _email, value);
        }

        private string _statusMessage;
        public string StatusMessage
        {
            get => _statusMessage;
            set => Set(ref _statusMessage, value);
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (Set(ref _isBusy, value))
                {
                    DeactivateCommand.RaiseCanExecuteChanged();
                    ActivateCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public RelayCommand ActivateCommand { get; }
        public RelayCommand DeactivateCommand { get; }

        /// <summary>Abre {BaseUrl}/quote en el navegador -- quien necesita una licencia pasa por Cotización, no por soporte directo (mismo criterio que la landing pública).</summary>
        public RelayCommand QuoteCommand { get; }
        public RelayCommand CloseCommand { get; }

        private void OpenQuotePage()
        {
            var url = (_client.BaseUrl ?? string.Empty).TrimEnd('/') + "/quote";
            try
            {
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                StatusMessage = "No se pudo abrir la página de cotización: " + ex.Message;
            }
        }

        private async Task ActivateAsync()
        {
            StatusMessage = null;

            if (string.IsNullOrWhiteSpace(LicenseKey))
            {
                StatusMessage = "Completa la clave de licencia.";
                return;
            }

            if (!PersonNameValidator.TryNormalize(FullName, out string fullName, out string nameError))
            {
                StatusMessage = nameError;
                return;
            }

            if (!EmailValidator.TryNormalize(Email, out string email, out string emailError))
            {
                StatusMessage = emailError;
                return;
            }

            IsBusy = true;
            _cts = new CancellationTokenSource();
            try
            {
                await _client.ActivateAsync(LicenseKey, fullName, email, _cts.Token).ConfigureAwait(true);
                _profileStore.Save(fullName, email);
                StatusMessage = "Licencia activada.";
                RestartReason = "Licencia activada correctamente.";
                RequestClose?.Invoke(true);
            }
            catch (Http.LicenseApiClientException ex)
            {
                StatusMessage = ex.Message;
            }
            catch (Exception ex)
            {
                StatusMessage = "No se pudo activar: " + ex.Message;
            }
            finally
            {
                IsBusy = false;
                _cts.Dispose();
                _cts = null;
            }
        }

        private async Task DeactivateAsync()
        {
            var confirm = MessageBox.Show(
                "¿Liberar este PC? Dejarás de poder usar GVR Tools aquí hasta volver a activar.",
                "Desactivar este PC",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            IsBusy = true;
            StatusMessage = null;
            try
            {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
                {
                    await _client.DeactivateAsync(cts.Token).ConfigureAwait(true);
                }

                StatusMessage = "PC liberado.";
                // Auditoría del sistema: antes esto solo refrescaba el estado en pantalla -- la
                // cinta de Revit, armada al arrancar con las entitlements de ANTES de desactivar,
                // seguía mostrando herramientas ya no licenciadas hasta un reinicio manual. Ahora
                // pide reinicio igual que tras activar, para que la cinta quede consistente.
                RestartReason = "PC liberado correctamente.";
                RequestClose?.Invoke(true);
            }
            catch (Exception ex)
            {
                StatusMessage = "No se pudo desactivar: " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}
