using System;
using System.Threading;
using System.Threading.Tasks;
using GvrTools.Licensing.Entitlements;
using GvrTools.Licensing.Http.Dto;

namespace GvrTools.Licensing
{
    /// <summary>
    /// Singleton de proceso del add-in: un LicenseClient compartido por App, ribbon y tools.
    /// </summary>
    public static class LicenseRuntime
    {
        /// <summary>
        /// Cada cuánto se consulta al servidor mientras Revit está abierto.
        /// Un cambio de plan/overrides o un kick en admin se refleja como máximo en este
        /// intervalo (no hay push). 5 s: lo más cerca de “tiempo real” sin inundar el API
        /// (varios Revit abiertos × poll).
        /// </summary>
        public static readonly TimeSpan SessionPollInterval = TimeSpan.FromSeconds(5);

        private static readonly object Gate = new object();
        private static LicenseClient _client;
        private static int _initialized;
        private static Timer _sessionWatchTimer;
        private static SynchronizationContext _uiContext;
        private static int _reactivationPromptShown;
        private static Action<string> _onSessionRevokedUi;

        public static LicenseClient Client
        {
            get
            {
                EnsureInitialized();
                return _client;
            }
        }

        public static IEntitlementService Entitlements => Client.Entitlements;

        public static bool IsLicensed => Client.IsLicensed;

        public static bool NeedsReactivation => _client != null && _client.NeedsReactivation;

        public static string ReactivationReason => _client?.ReactivationReason;

        public static void EnsureInitialized()
        {
            if (_client != null)
                return;

            lock (Gate)
            {
                if (_client != null)
                    return;

                _client = new LicenseClient();
                Interlocked.Exchange(ref _initialized, 1);
            }
        }

        /// <summary>
        /// Heartbeat periódico: detecta kick/suspensión sin reiniciar Revit.
        /// <paramref name="onSessionRevokedUi"/> se llama en el hilo UI como máximo una vez
        /// hasta que el usuario reactive.
        /// </summary>
        public static void StartSessionWatch(
            SynchronizationContext uiContext,
            Action<string> onSessionRevokedUi)
        {
            EnsureInitialized();
            _uiContext = uiContext;
            _onSessionRevokedUi = onSessionRevokedUi;

            _client.SessionInvalidated -= OnSessionInvalidated;
            _client.SessionInvalidated += OnSessionInvalidated;
            _client.SessionRestored -= OnSessionRestored;
            _client.SessionRestored += OnSessionRestored;

            if (_sessionWatchTimer != null)
                return;

            // Primer tick tras el intervalo (WarmupAsync ya corre al arranque).
            _sessionWatchTimer = new Timer(
                _ => _ = PollSessionSafeAsync(),
                null,
                SessionPollInterval,
                SessionPollInterval);
        }

        public static void StopSessionWatch()
        {
            var timer = Interlocked.Exchange(ref _sessionWatchTimer, null);
            timer?.Dispose();

            if (_client != null)
            {
                _client.SessionInvalidated -= OnSessionInvalidated;
                _client.SessionRestored -= OnSessionRestored;
            }

            _onSessionRevokedUi = null;
            _uiContext = null;
            Interlocked.Exchange(ref _reactivationPromptShown, 0);
        }

        /// <summary>
        /// Heartbeat con timeout corto (~8s). Renueva JWT + gracia. Nunca tumba el add-in:
        /// fallos de red se tragan; 401/403/404 limpian cache y marcan NeedsReactivation.
        /// </summary>
        public static async Task WarmupAsync(CancellationToken externalCt = default)
        {
            EnsureInitialized();

            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt))
            {
                cts.CancelAfter(TimeSpan.FromSeconds(8));
                try
                {
                    var renewed = await _client.TryHeartbeatAsync(cts.Token).ConfigureAwait(false);

                    // UI_FREEMIUM_PLAN.md §4.1: PC limpio (nunca activó nada, TryHeartbeatAsync
                    // devuelve false sin llamar a la red porque no hay token) intenta el plan free
                    // antes de dejar las tools ocultas. NeedsReactivation es distinto -- esa persona
                    // SÍ tuvo una licencia y el servidor la rechazó (kick/suspensión); no hay que
                    // inventarle una free por detrás, tiene que reactivar a propósito.
                    if (!renewed && !_client.IsLicensed && !_client.NeedsReactivation)
                    {
                        await _client.TryActivateFreeAsync(cts.Token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    // timeout o cancelación externa: seguir con cache
                }
                catch (Http.LicenseApiClientException)
                {
                    // ya limpió cache + NeedsReactivation en TryHeartbeatAsync
                }
                catch (Exception)
                {
                    // no tumbar OnStartup
                }
            }
        }

        /// <summary>
        /// Consulta updates tras el arranque. Null si no hay update o falla la red.
        /// Timeout propio (~4s) para no alargar el startup.
        /// </summary>
        public static async Task<UpdateCheckResponse> TryCheckForUpdateAsync(
            string currentVersion,
            string revitVersion,
            CancellationToken externalCt = default)
        {
            EnsureInitialized();

            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt))
            {
                cts.CancelAfter(TimeSpan.FromSeconds(4));
                try
                {
                    return await _client.TryCheckForUpdateAsync(currentVersion, revitVersion, cts.Token)
                        .ConfigureAwait(false);
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        /// <summary>Solo tests / reinicio controlado.</summary>
        public static void ResetForTests()
        {
            StopSessionWatch();
            lock (Gate)
            {
                _client?.Dispose();
                _client = null;
                Interlocked.Exchange(ref _initialized, 0);
            }
        }

        private static async Task PollSessionSafeAsync()
        {
            try
            {
                if (_client == null || _client.NeedsReactivation)
                    return;

                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                {
                    await _client.TryHeartbeatAsync(cts.Token).ConfigureAwait(false);
                }
            }
            catch (Http.LicenseApiClientException)
            {
                // SessionInvalidated ya se disparó desde MarkNeedsReactivation.
            }
            catch (Exception)
            {
                // Red / timeout: no molestar; se reintenta en el próximo intervalo.
            }
        }

        private static void OnSessionRestored()
        {
            Interlocked.Exchange(ref _reactivationPromptShown, 0);
        }

        private static void OnSessionInvalidated()
        {
            if (Interlocked.Exchange(ref _reactivationPromptShown, 1) != 0)
                return;

            var reason = ReactivationReason
                ?? "Este PC fue desvinculado o la licencia ya no es válida. Activa de nuevo con tu clave de licencia.";
            var callback = _onSessionRevokedUi;
            if (callback == null)
                return;

            void Show()
            {
                try
                {
                    callback(reason);
                }
                catch
                {
                    // ignore UI failures
                }
            }

            if (_uiContext != null)
                _uiContext.Post(_ => Show(), null);
            else
                Show();
        }
    }
}
