using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using GvrTools.Licensing.Http.Dto;
using GvrTools.UI.Mvvm;

namespace GvrTools.Licensing.Activation
{
    public sealed class UpdateAvailableViewModel : ObservableObject
    {
        private readonly LicenseClient _client;
        private readonly UpdateCheckResponse _update;

        public UpdateAvailableViewModel(LicenseClient client, UpdateCheckResponse update, string currentVersion)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _update = update ?? throw new ArgumentNullException(nameof(update));
            CurrentVersion = currentVersion ?? "—";
            LatestVersion = update.LatestVersion ?? "—";
            Notes = string.IsNullOrWhiteSpace(update.ReleaseNotes)
                ? "Hay una versión nueva de GVR Tools lista para instalar."
                : update.ReleaseNotes;

            DownloadCommand = new RelayCommand(async () => await DownloadAsync(), () => !IsBusy);
            LaterCommand = new RelayCommand(() => RequestClose?.Invoke());
        }

        public event Action RequestClose;

        public string CurrentVersion { get; }
        public string LatestVersion { get; }
        public string Notes { get; }

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
                    DownloadCommand.RaiseCanExecuteChanged();
            }
        }

        public RelayCommand DownloadCommand { get; }
        public RelayCommand LaterCommand { get; }

        private async Task DownloadAsync()
        {
            IsBusy = true;
            StatusMessage = null;
            try
            {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
                {
                    var url = await _client.ResolveUpdateDownloadUrlAsync(_update, cts.Token).ConfigureAwait(true);
                    if (string.IsNullOrWhiteSpace(url))
                    {
                        StatusMessage = "No se pudo obtener el enlace de descarga.";
                        return;
                    }

                    Process.Start(new ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                    RequestClose?.Invoke();
                }
            }
            catch (Exception ex)
            {
                StatusMessage = "No se pudo abrir la descarga: " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}
