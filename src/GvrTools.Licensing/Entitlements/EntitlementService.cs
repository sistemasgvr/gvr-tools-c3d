using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using GvrTools.Licensing.Crypto;
using GvrTools.Licensing.Http.Dto;
using GvrTools.Licensing.Storage;

namespace GvrTools.Licensing.Entitlements
{
    /// <summary>
    /// Lee el blob cacheado verificado. TryConsume decrementa remaining local y encola usage
    /// para reconciliar online (docs/LICENSING_PLAN.md, reglas de consumo offline).
    /// </summary>
    public sealed class EntitlementService : IEntitlementService
    {
        private readonly IEntitlementSignatureVerifier _verifier;
        private readonly FileUsageQueueStore _usageQueue;
        private readonly object _gate = new object();

        private EntitlementBlob _blob;
        private Dictionary<string, string> _features = new Dictionary<string, string>(StringComparer.Ordinal);
        private string _deviceFingerprint;

        // "Piso" de reloj monótono contra manipulación del reloj del sistema -- ver AdvanceClockFloor.
        private DateTimeOffset _clockFloorUtc = DateTimeOffset.MinValue;

        public EntitlementService(IEntitlementSignatureVerifier verifier, FileUsageQueueStore usageQueue = null)
        {
            _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
            _usageQueue = usageQueue ?? new FileUsageQueueStore();
        }

        public EntitlementBlob CurrentBlob
        {
            get { lock (_gate) return _blob; }
        }

        public bool IsLicensed
        {
            get
            {
                lock (_gate)
                {
                    return _blob != null && IsWithinGrace(_blob);
                }
            }
        }

        public string PlanCode
        {
            get
            {
                lock (_gate)
                {
                    return _blob?.PlanCode;
                }
            }
        }

        public DateTimeOffset? OfflineUntilUtc
        {
            get
            {
                lock (_gate)
                {
                    return TryParseUtc(_blob?.OfflineUntilUtc, out var dt) ? dt : (DateTimeOffset?)null;
                }
            }
        }

        /// <summary>DeviceId (GUID del servidor) del blob actualmente cargado, o null si no hay ninguno.</summary>
        public string DeviceId
        {
            get { lock (_gate) return _blob?.DeviceId; }
        }

        /// <summary>
        /// Carga blob firmado; false si firma inválida, gracia vencida, o (cuando se pasa
        /// <paramref name="expectedDeviceId"/>) el blob pertenece a un dispositivo distinto del
        /// esperado -- ver <see cref="Storage.FileDevicePinStore"/> para el porqué de este chequeo.
        /// </summary>
        public bool TryApplySignedBlob(string rawJson, byte[] signature, string deviceFingerprint = null, string expectedDeviceId = null)
        {
            if (!_verifier.TryVerify(rawJson, signature, out var blob) || blob == null)
                return false;

            // Solo se aplica cuando el llamador YA tiene una marca local con la que comparar (ver
            // FileDevicePinStore) -- deja pasar el "trust on first use" implícito cuando todavía no
            // existe ninguna marca (expectedDeviceId null/vacío), pero rechaza de plano un blob cuyo
            // DeviceId no coincide con el que esta instalación ya tenía fijado, del mismo modo que una
            // firma inválida: como si el blob nunca hubiera pasado la verificación.
            if (!string.IsNullOrEmpty(expectedDeviceId) &&
                !string.Equals(blob.DeviceId, expectedDeviceId, StringComparison.Ordinal))
                return false;

            if (!IsWithinGrace(blob))
                return false;

            lock (_gate)
            {
                _blob = blob;
                _features = ToDictionary(blob);
                if (!string.IsNullOrEmpty(deviceFingerprint))
                    _deviceFingerprint = deviceFingerprint;
            }

            return true;
        }

        public void Clear()
        {
            lock (_gate)
            {
                _blob = null;
                _features = new Dictionary<string, string>(StringComparer.Ordinal);
            }
        }

        public bool CanUse(string featureCode)
        {
            if (string.IsNullOrEmpty(featureCode)) return true;

            lock (_gate)
            {
                if (_blob == null || !IsWithinGrace(_blob)) return false;
                if (!_features.TryGetValue(featureCode, out var value)) return false;
                return IsTruthy(value);
            }
        }

        public int Remaining(string featureCode)
        {
            lock (_gate)
            {
                if (_blob == null || !IsWithinGrace(_blob)) return 0;
                if (!_features.TryGetValue(featureCode, out var value)) return 0;
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) return 0;
                return n;
            }
        }

        public string GetString(string featureCode)
        {
            if (string.IsNullOrEmpty(featureCode)) return null;

            lock (_gate)
            {
                if (_blob == null || !IsWithinGrace(_blob)) return null;
                return _features.TryGetValue(featureCode, out var value) ? value : null;
            }
        }

        public int QuotaLimit(string featureCode)
        {
            if (string.IsNullOrEmpty(featureCode)) return 0;

            lock (_gate)
            {
                if (_blob == null || !IsWithinGrace(_blob)) return 0;
                var limitCode = featureCode + ".limit";
                if (!_features.TryGetValue(limitCode, out var value)) return 0;
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) return 0;
                return n;
            }
        }

        public bool TryConsume(string featureCode, int quantity)
        {
            if (quantity <= 0) return true;

            bool changed;
            lock (_gate)
            {
                if (_blob == null || !IsWithinGrace(_blob)) return false;
                if (!_features.TryGetValue(featureCode, out var value)) return false;
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var remaining))
                    return false;

                if (remaining == -1)
                {
                    EnqueueUsageLocked(featureCode, quantity);
                    return true;
                }

                if (remaining < quantity) return false;

                remaining -= quantity;
                _features[featureCode] = remaining.ToString(CultureInfo.InvariantCulture);
                UpdateFeatureInBlob(featureCode, _features[featureCode]);
                EnqueueUsageLocked(featureCode, quantity);
                changed = true;
            }

            // Fuera del lock -- ver comentario en el campo Changed sobre por qué esto vive aparte del
            // blob firmado.
            if (changed) Changed?.Invoke();
            return true;
        }

        /// <summary>Tras un report exitoso online, alinea remaining con el servidor.</summary>
        public void SetRemaining(string featureCode, int? remaining)
        {
            if (remaining == null || string.IsNullOrEmpty(featureCode)) return;

            lock (_gate)
            {
                if (_blob == null) return;
                _features[featureCode] = remaining.Value.ToString(CultureInfo.InvariantCulture);
                UpdateFeatureInBlob(featureCode, _features[featureCode]);
            }

            Changed?.Invoke();
        }

        /// <summary>
        /// Se dispara cada vez que TryConsume/SetRemaining cambian un valor numérico EN MEMORIA. El
        /// blob firmado en sí (EntitlementJson + su firma ECDSA) nunca puede editarse y re-guardarse
        /// tal cual -- la firma cubre el JSON original exacto, y el cliente no tiene la clave privada
        /// del servidor para re-firmarlo tras una mutación. Por eso el consumo local no se persistía
        /// antes (se perdía en cada reinicio de Revit, permitiendo re-consumir la cuota completa
        /// reiniciando mientras se está offline). El host (LicenseClient) escucha este evento y
        /// guarda un snapshot de <see cref="SnapshotFeatures"/> en un campo NO firmado y APARTE del
        /// envelope -- una "capa local" que en la próxima carga se reaplica sobre el blob firmado
        /// intacto, pero SOLO para reducir remaining, nunca para aumentarlo (ver
        /// LicenseClient.ApplyPersistedLocalOverrides), así que no representa ninguna superficie
        /// nueva de manipulación: en el peor caso (el archivo de overrides se borra a mano) el
        /// comportamiento vuelve a ser exactamente el de antes de este fix.
        /// </summary>
        public event Action Changed;

        /// <summary>Copia de los valores de feature actuales, para que el host los persista tras <see cref="Changed"/>.</summary>
        public IReadOnlyDictionary<string, string> SnapshotFeatures()
        {
            lock (_gate)
            {
                return new Dictionary<string, string>(_features, StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Reaplica valores guardados localmente (ver <see cref="Changed"/>) sobre el blob recién
        /// cargado, uno por uno, y SOLO cuando el valor local es numérico y menor o igual al que ya
        /// trae el blob -- nunca puede usarse para otorgar más cuota de la que el servidor firmó, solo
        /// para recordar consumo local que el servidor todavía no confirmó. Debe llamarse ANTES de que
        /// nada más lea Remaining/CanUse, justo después de un TryApplySignedBlob exitoso.
        /// </summary>
        public void ApplyPersistedLocalOverrides(IReadOnlyDictionary<string, string> overrides)
        {
            if (overrides == null || overrides.Count == 0) return;

            lock (_gate)
            {
                if (_blob == null) return;

                foreach (var pair in overrides)
                {
                    if (!_features.TryGetValue(pair.Key, out var current)) continue;
                    if (!int.TryParse(current, NumberStyles.Integer, CultureInfo.InvariantCulture, out var currentValue))
                        continue;
                    if (currentValue == -1) continue; // Ilimitado: nada que recortar.
                    if (!int.TryParse(pair.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var overrideValue))
                        continue;
                    if (overrideValue < 0 || overrideValue >= currentValue) continue;

                    _features[pair.Key] = overrideValue.ToString(CultureInfo.InvariantCulture);
                    UpdateFeatureInBlob(pair.Key, _features[pair.Key]);
                }
            }
        }

        /// <summary>
        /// Avanza el piso de reloj monótono con un valor observado (típicamente el "ahora" persistido
        /// de la última corrida, o el "ahora" real justo antes de guardarlo). Nunca retrocede: si
        /// <paramref name="observedUtc"/> es anterior al piso actual, se ignora.
        ///
        /// Contra qué protege: <see cref="IsWithinGrace"/> comparaba <c>DateTimeOffset.UtcNow</c>
        /// directo contra OfflineUntilUtc -- atrasar el reloj del sistema (sin privilegios de admin
        /// si "hora automática" está desactivada, el caso normal en un PC personal) renovaba la
        /// gracia offline indefinidamente sin volver a contactar al servidor. Con el piso, "ahora
        /// efectivo" es <c>max(reloj real, último ahora legítimo que ya vimos)</c> -- atrasar el
        /// reloj después de eso ya no hace retroceder el efectivo.
        ///
        /// Límite conocido: como el piso se persiste en disco (LicenseClient), sigue siendo posible
        /// atrasar el reloj Y editar/borrar el archivo persistido a la vez -- esto sube el costo del
        /// ataque de "cambiar una configuración" a "manipular un archivo aparte además", no lo hace
        /// matemáticamente imposible. Ninguna protección puramente local puede serlo sin depender de
        /// una fuente de tiempo externa confiable, que el propósito de la gracia OFFLINE excluye por
        /// definición.
        /// </summary>
        public void AdvanceClockFloor(DateTimeOffset observedUtc)
        {
            lock (_gate)
            {
                if (observedUtc > _clockFloorUtc)
                    _clockFloorUtc = observedUtc;
            }
        }

        /// <summary>"Ahora" efectivo tras aplicar el piso monótono -- para que el host lo vuelva a persistir.</summary>
        public DateTimeOffset EffectiveNowUtc
        {
            get { lock (_gate) return Max(DateTimeOffset.UtcNow, _clockFloorUtc); }
        }

        public void SetDeviceFingerprint(string fingerprint)
        {
            lock (_gate)
            {
                _deviceFingerprint = fingerprint;
            }
        }

        private void EnqueueUsageLocked(string featureCode, int quantity)
        {
            var fp = _deviceFingerprint ?? string.Empty;
            _usageQueue.Enqueue(new UsageEventDto
            {
                DeviceFingerprint = fp,
                EventId = Guid.NewGuid(),
                FeatureCode = featureCode,
                Quantity = quantity,
                OccurredAtUtc = DateTime.UtcNow
            });
        }

        private void UpdateFeatureInBlob(string code, string value)
        {
            if (_blob?.Features == null) return;
            foreach (var entry in _blob.Features)
            {
                if (string.Equals(entry.Code, code, StringComparison.OrdinalIgnoreCase))
                {
                    entry.Value = value;
                    return;
                }
            }
        }

        private static Dictionary<string, string> ToDictionary(EntitlementBlob blob)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (blob.Features == null) return map;
            foreach (var entry in blob.Features)
            {
                if (entry == null || string.IsNullOrEmpty(entry.Code)) continue;
                map[entry.Code] = entry.Value ?? string.Empty;
            }
            return map;
        }

        private bool IsWithinGrace(EntitlementBlob blob)
        {
            if (!TryParseUtc(blob.OfflineUntilUtc, out var until)) return false;
            return EffectiveNowUtc <= until;
        }

        private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a >= b ? a : b;

        private static bool TryParseUtc(string value, out DateTimeOffset dt)
        {
            return DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out dt);
        }

        private static bool IsTruthy(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            if (string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)) return false;
            if (string.Equals(value, "0", StringComparison.OrdinalIgnoreCase)) return false;
            if (string.Equals(value, "no", StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }
    }
}
