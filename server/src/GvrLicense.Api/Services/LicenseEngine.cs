using System.Globalization;
using System.Text;
using System.Text.Json;
using GvrLicense.Contracts;
using GvrLicense.Domain.Entities;
using GvrLicense.Domain.LicenseKeys;
using GvrLicense.Domain.Validation;
using GvrLicense.Domain.Versioning;
using GvrLicense.Infrastructure;
using GvrLicense.Infrastructure.Signing;
using GvrLicense.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace GvrLicense.Api.Services;

/// <summary>
/// El motor de negocio detrás de /v1/* (docs/LICENSING_PLAN.md, Pieza 1 "API mínima" + Pieza 3
/// "Servidor manda"). Un caso de uso por método público; cada uno hace su propia validación y
/// lanza <see cref="LicenseApiException"/> con el status HTTP correcto en vez de devolver
/// resultados ambiguos.
/// </summary>
public sealed class LicenseEngine(
    LicenseDbContext db,
    IEntitlementSigner signer,
    JwtSessionTokenService jwt,
    IReleaseArtifactStore artifacts)
{
    // Opciones por defecto (PascalCase, sin naming policy) a propósito: deben coincidir byte a byte
    // con lo que el cliente net48 espera de DataContractJsonSerializer (ver
    // src/GvrTools.Licensing/Crypto/EcdsaEntitlementSignatureVerifier.cs y
    // GvrLicense.Contracts/EntitlementBlob.cs).
    private static readonly JsonSerializerOptions WireJsonOptions = new();

    public async Task<ActivateResponse> ActivateAsync(ActivateRequest request, string? clientIp, CancellationToken ct)
    {
        var normalizedKey = LicenseKeyGenerator.Normalize(request.LicenseKey);
        if (!LicenseKeyGenerator.TryValidateFormat(normalizedKey))
        {
            throw new LicenseApiException(400, "Formato de license key inválido.");
        }

        if (!PersonNameValidator.TryNormalize(request.UserFullName, out var userFullName, out var nameError))
        {
            throw new LicenseApiException(400, nameError);
        }

        if (!EmailValidator.TryNormalize(request.UserEmail, out var userEmail, out var emailError))
        {
            throw new LicenseApiException(400, emailError);
        }

        var licenseId = await db.Licenses
            .Where(l => l.Key == normalizedKey)
            .Select(l => (Guid?)l.Id)
            .FirstOrDefaultAsync(ct);

        if (licenseId is null)
        {
            throw new LicenseApiException(404, "Licencia no encontrada.");
        }

        // Bloqueo de fila a nivel de base de datos (SELECT ... FOR UPDATE) ANTES de leer Devices:
        // sin esto, dos activaciones concurrentes para la misma licencia (dos dispositivos distintos,
        // justo en el límite de asientos) podían leer el mismo conteo "por debajo del límite" antes
        // de que ninguna de las dos confirmara, y las dos pasar la validación -- el índice único
        // (LicenseId, Fingerprint) solo protege contra el MISMO fingerprint duplicado, no contra
        // exceder MaxUsers/max_devices_per_user por una carrera. Se bloquea PRIMERO y recién después
        // se cargan Devices, para que la segunda transacción, al obtener el bloqueo, vea ya
        // confirmados los cambios de la primera.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM license WHERE id = {licenseId.Value} FOR UPDATE", ct);

        var license = await db.Licenses
            .Include(l => l.Plan)
            .Include(l => l.Devices)
            .FirstOrDefaultAsync(l => l.Id == licenseId.Value, ct);

        if (license is null)
        {
            throw new LicenseApiException(404, "Licencia no encontrada.");
        }

        EnsureLicenseUsable(license);

        var companyUser = await FindOrCreateCompanyUserAsync(license.CustomerId, userFullName, userEmail, ct);

        var device = license.Devices.FirstOrDefault(d => d.Fingerprint == request.DeviceFingerprint);
        if (device is null)
        {
            var devicesOfThisUser = license.Devices.Where(d => d.CompanyUserId == companyUser.Id).ToList();

            // El seat se cuenta por persona, no por dispositivo (docs/LICENSING_PLAN.md, "Métodos
            // de suscripción"): si esta persona ya tiene otro dispositivo en la misma licencia, un
            // dispositivo más no gasta un seat nuevo -- pero sí está topado por cuántos dispositivos
            // puede tener UNA persona ("un usuario puede usar la licencia en uno o más dispositivos
            // según lo configuremos"), vía el feature seat.max_devices_per_user (mismo patrón que el
            // resto del catálogo: por defecto 1 si el plan no lo define, -1 = ilimitado).
            var maxDevicesPerUser = ParseFeatureInt(GetEffectiveFeatures(license), "seat.max_devices_per_user", defaultValue: 1);
            if (maxDevicesPerUser != -1 && devicesOfThisUser.Count >= maxDevicesPerUser)
            {
                throw new LicenseApiException(403,
                    $"{userEmail} ya activó en {maxDevicesPerUser} dispositivo(s), el máximo que permite el plan. Desactiva uno antes de activar en este PC.");
            }

            var distinctUsersExcludingThis = license.Devices
                .Select(d => d.CompanyUserId)
                .Where(id => id != companyUser.Id)
                .Distinct()
                .Count();

            if (devicesOfThisUser.Count == 0 && distinctUsersExcludingThis >= license.MaxUsers)
            {
                throw new LicenseApiException(403,
                    $"Esta licencia ya tiene {license.MaxUsers} usuario(s) activo(s). Libera un seat antes de activar para {userEmail}.");
            }

            device = new Device
            {
                Id = Guid.NewGuid(),
                LicenseId = license.Id,
                CompanyUserId = companyUser.Id,
                Fingerprint = request.DeviceFingerprint,
                DisplayName = request.DeviceName,
                ActivatedAtUtc = DateTimeOffset.UtcNow,
                LastSeenUtc = DateTimeOffset.UtcNow,
                LastIp = clientIp,
                SeenCount = 1
            };
            db.Devices.Add(device);
        }
        else
        {
            // Mismo PC, persona distinta activando encima: reasigna el dispositivo a quien acaba de
            // activar -- el dueño de un fingerprint es quien lo usó por última vez.
            // Misma regla de seats que al crear un device nuevo (MaxUsers + max_devices_per_user).
            if (device.CompanyUserId != companyUser.Id)
            {
                var devicesOfThisUser = license.Devices
                    .Where(d => d.Id != device.Id && d.CompanyUserId == companyUser.Id)
                    .ToList();

                var maxDevicesPerUser = ParseFeatureInt(
                    GetEffectiveFeatures(license), "seat.max_devices_per_user", defaultValue: 1);
                if (maxDevicesPerUser != -1 && devicesOfThisUser.Count >= maxDevicesPerUser)
                {
                    throw new LicenseApiException(403,
                        $"{userEmail} ya activó en {maxDevicesPerUser} dispositivo(s), el máximo que permite el plan. Desactiva uno antes de activar en este PC.");
                }

                var distinctUsersAfterReassign = license.Devices
                    .Where(d => d.Id != device.Id)
                    .Select(d => d.CompanyUserId)
                    .Append(companyUser.Id)
                    .Distinct()
                    .Count();

                if (distinctUsersAfterReassign > license.MaxUsers)
                {
                    throw new LicenseApiException(403,
                        $"Esta licencia ya tiene {license.MaxUsers} usuario(s) activo(s). Libera un seat antes de activar para {userEmail}.");
                }

                device.CompanyUserId = companyUser.Id;
            }

            TouchDevice(device, clientIp);
            if (!string.IsNullOrWhiteSpace(request.DeviceName))
            {
                device.DisplayName = request.DeviceName;
            }
        }

        // UI_FREEMIUM_PLAN.md §4.2: hasta ahora activate con key de pago no dejaba rastro propio
        // (solo el trigger de status change). Sin esto, "quién activó qué y cuándo" era invisible
        // en Auditoría para el caso más común de todos.
        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            LicenseId = license.Id,
            Actor = userEmail,
            Action = "license.activate",
            DetailsJson = JsonSerializer.Serialize(new { fingerprint = request.DeviceFingerprint, deviceName = request.DeviceName, ip = clientIp }),
            OccurredAtUtc = DateTimeOffset.UtcNow
        });

        await EnsureCurrentPeriodCountersAsync(license, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var (json, signature) = await BuildSignedBlobAsync(license, device, ct);
        var accessToken = jwt.Issue(license.Id, device.Id);

        return new ActivateResponse
        {
            AccessToken = accessToken,
            EntitlementJson = json,
            EntitlementSignatureBase64 = Convert.ToBase64String(signature)
        };
    }

    /// <summary>
    /// UI_FREEMIUM_PLAN.md §2.2/§4.1: primer arranque sin license.dat válido. A diferencia de
    /// <see cref="ActivateAsync"/>, no hay ninguna key que ya identifique un cliente -- el mismo
    /// fingerprint siempre resuelve a la MISMA licencia (free o de pago si ya hizo upgrade), nunca
    /// crea una free nueva por reinstalación (§2.2 "Anti-reinstalación").
    /// </summary>
    public async Task<ActivateResponse> ActivateFreeAsync(ActivateFreeRequest request, string? clientIp, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.DeviceFingerprint))
        {
            throw new LicenseApiException(400, "Fingerprint requerido.");
        }

        // Devices.Fingerprint es único solo DENTRO de una licencia (LicenseId, Fingerprint) -- a
        // propósito, porque el mismo fingerprint legítimamente aparece en más de una licencia cuando
        // una máquina pasa de free a de pago (ActivateAsync crea un Device nuevo bajo la licencia de
        // pago; el Device viejo bajo la free queda tal cual). Eso significa que NINGÚN índice único de
        // base de datos por sí solo evita que dos altas free concurrentes con el mismo fingerprint
        // (antes de que cualquiera de las dos confirme) lean ambas "no existe" y cada una cree su
        // propia licencia free nueva -- exactamente lo que "anti-reinstalación" promete evitar. Se
        // serializa con un advisory lock de Postgres por fingerprint (no por fila -- todavía no existe
        // ninguna fila que bloquear en la primera alta), liberado solo al confirmar/revertir la
        // transacción.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtext({request.DeviceFingerprint})::bigint)", ct);

        var existingDevice = await db.Devices
            .Include(d => d.License).ThenInclude(l => l!.Plan)
            .FirstOrDefaultAsync(d => d.Fingerprint == request.DeviceFingerprint, ct);

        if (existingDevice != null)
        {
            var reusedLicense = existingDevice.License!;
            EnsureLicenseUsable(reusedLicense);

            // Reuso (anti-reinstalación): no escribe otro audit_log, pero sí refresca IP / last_seen.
            TouchDevice(existingDevice, clientIp);
            await EnsureCurrentPeriodCountersAsync(reusedLicense, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            var (reusedJson, reusedSignature) = await BuildSignedBlobAsync(reusedLicense, existingDevice, ct);
            return new ActivateResponse
            {
                AccessToken = jwt.Issue(reusedLicense.Id, existingDevice.Id),
                EntitlementJson = reusedJson,
                EntitlementSignatureBase64 = Convert.ToBase64String(reusedSignature)
            };
        }

        var freePlan = await db.Plans.FirstOrDefaultAsync(p => p.Code == FreePlanCode && p.IsActive, ct);
        if (freePlan is null)
        {
            // Invariante operacional del plan (§2.2): no debe pasar en operación normal, pero si el
            // plan free se desactivó a propósito o por error, esto tiene que fallar con un mensaje
            // claro -- nunca inventar un plan fantasma en código.
            await AuditDeniedAsync(clientIp, request.DeviceFingerprint, "Plan free ausente o desactivado.", ct);
            // Confirma antes de lanzar: si no, el rollback automático del "await using tx" al
            // propagarse la excepción se llevaría también el registro de auditoría recién escrito.
            await tx.CommitAsync(ct);
            throw new LicenseApiException(503, "Registro gratuito temporalmente no disponible. Contacta a soporte.");
        }

        // Kill switch de emergencia (DDoS): IsActive sigue true para el catálogo; solo corta altas Free.
        // 403 (no 503): el add-in limpia caché en el próximo heartbeat; 503 se traga como gracia offline.
        if (freePlan.ServiceSuspended)
        {
            await AuditDeniedAsync(clientIp, request.DeviceFingerprint, "Plan free con servicio suspendido.", ct);
            // Ver el comentario equivalente arriba: confirmar antes de lanzar para no perder el
            // registro de auditoría en el rollback automático.
            await tx.CommitAsync(ct);
            throw new LicenseApiException(403, ServiceSuspendedMessage(freePlan));
        }

        var freeCustomer = await db.Customers.FirstOrDefaultAsync(c => c.CompanyName == FreeCustomerName, ct);
        if (freeCustomer is null)
        {
            freeCustomer = new Customer
            {
                Id = Guid.NewGuid(),
                CompanyName = FreeCustomerName,
                ContactName = "GVR Tools",
                ContactEmail = "-",
                IsActive = true,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            db.Customers.Add(freeCustomer);
        }

        CompanyUser companyUser;
        if (PersonNameValidator.TryNormalize(request.UserFullName, out var fullName, out _) &&
            EmailValidator.TryNormalize(request.UserEmail, out var email, out _))
        {
            companyUser = await FindOrCreateCompanyUserAsync(freeCustomer.Id, fullName, email, ct);
        }
        else
        {
            // Sin nombre/correo real: cada fingerprint es su propia "persona" -- un correo sintético
            // estable por fingerprint para que reintentar desde el mismo PC nunca duplique el
            // CompanyUser (aunque en la práctica ya no debería llegar aquí una segunda vez: el mismo
            // fingerprint sale por la rama "existingDevice" de arriba).
            var fingerprintTag = request.DeviceFingerprint[..Math.Min(20, request.DeviceFingerprint.Length)];
            var syntheticEmail = $"free-{fingerprintTag}@device.local";
            var displayName = string.IsNullOrWhiteSpace(request.DeviceName) ? "Usuario gratuito" : request.DeviceName!;
            companyUser = await FindOrCreateCompanyUserAsync(freeCustomer.Id, displayName, syntheticEmail, ct);
        }

        string key;
        for (var attempt = 0; ; attempt++)
        {
            key = LicenseKeyGenerator.Generate();
            if (!await db.Licenses.AnyAsync(l => l.Key == key, ct)) break;
            if (attempt >= 5)
            {
                throw new LicenseApiException(503, "No se pudo generar una licencia gratuita. Intenta de nuevo.");
            }
        }

        var license = new License
        {
            Id = Guid.NewGuid(),
            Key = key,
            CustomerId = freeCustomer.Id,
            PlanId = freePlan.Id,
            Status = LicenseStatus.Active,
            // "Permanente" (§2.2): no vence por fecha: solo por que el plan free se desactive o el
            // admin suspenda la licencia puntual.
            ValidUntil = DateTimeOffset.UtcNow.AddYears(100),
            MaxUsers = 1,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        db.Licenses.Add(license);

        var device = new Device
        {
            Id = Guid.NewGuid(),
            LicenseId = license.Id,
            CompanyUserId = companyUser.Id,
            Fingerprint = request.DeviceFingerprint,
            DisplayName = request.DeviceName,
            ActivatedAtUtc = DateTimeOffset.UtcNow,
            LastSeenUtc = DateTimeOffset.UtcNow,
            LastIp = clientIp,
            SeenCount = 1
        };
        db.Devices.Add(device);

        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            LicenseId = license.Id,
            Actor = "system",
            Action = "license.activate_free",
            DetailsJson = JsonSerializer.Serialize(new { fingerprint = request.DeviceFingerprint, ip = clientIp }),
            OccurredAtUtc = DateTimeOffset.UtcNow
        });

        // EnsureCurrentPeriodCountersAsync hace un INSERT en SQL crudo que referencia license_id
        // por FK -- a diferencia de ActivateAsync/HeartbeatAsync (donde license ya existía en la
        // fila), aquí license es una entidad nueva todavía sin persistir. Sin este SaveChangesAsync
        // primero, el INSERT crudo revienta con 23503 (violación de FK) porque la fila license
        // todavía no existe de verdad en Postgres.
        await db.SaveChangesAsync(ct);
        await EnsureCurrentPeriodCountersAsync(license, ct);
        await tx.CommitAsync(ct);

        var (json, signature) = await BuildSignedBlobAsync(license, device, ct);
        return new ActivateResponse
        {
            AccessToken = jwt.Issue(license.Id, device.Id),
            EntitlementJson = json,
            EntitlementSignatureBase64 = Convert.ToBase64String(signature)
        };
    }

    private const string FreePlanCode = "free";
    private const string FreeCustomerName = "GVR Free installs";

    /// <summary>Refresca last_seen / last_ip y suma un contacto (activate reuso o heartbeat).</summary>
    private static void TouchDevice(Device device, string? clientIp)
    {
        device.LastSeenUtc = DateTimeOffset.UtcNow;
        device.SeenCount = device.SeenCount <= 0 ? 1 : device.SeenCount + 1;
        if (!string.IsNullOrWhiteSpace(clientIp))
        {
            device.LastIp = clientIp;
        }
    }

    private async Task AuditDeniedAsync(string? clientIp, string fingerprint, string reason, CancellationToken ct)
    {
        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            LicenseId = null,
            Actor = "system",
            Action = "security.activate_free_denied",
            DetailsJson = JsonSerializer.Serialize(new { fingerprint, ip = clientIp, reason }),
            OccurredAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// licenseId/deviceId ya vienen autenticados: los extrajo el middleware AddJwtBearer de
    /// Program.cs de los claims del JWT (Authorization: Bearer), no se validan aquí -- ver
    /// Endpoints/V1Endpoints.cs.
    /// </summary>
    public async Task<HeartbeatResponse> HeartbeatAsync(Guid licenseId, Guid deviceId, HeartbeatRequest request, string? clientIp, CancellationToken ct)
    {
        var license = await db.Licenses
            .Include(l => l.Plan)
            .Include(l => l.Devices).ThenInclude(d => d.CompanyUser)
            .FirstOrDefaultAsync(l => l.Id == licenseId, ct);

        if (license is null)
        {
            throw new LicenseApiException(404, "Licencia no encontrada.");
        }

        var device = license.Devices.FirstOrDefault(d => d.Id == deviceId && d.Fingerprint == request.DeviceFingerprint);
        if (device is null)
        {
            throw new LicenseApiException(401,
                "Este PC fue desvinculado o ya no está autorizado. Activa de nuevo con tu clave de licencia.");
        }

        // Un CompanyUser desactivado desde Admin -> Customers -> Members ("Desactivar") no debe poder
        // seguir renovando su sesión indefinidamente vía heartbeat -- antes de este chequeo, el botón
        // "Desactivar" no bloqueaba nada en la práctica: solo impedía NUEVAS activaciones
        // (FindOrCreateCompanyUserAsync), pero un device ya activado seguía recibiendo un JWT nuevo de
        // 14 días en cada heartbeat sin límite.
        if (device.CompanyUser is { IsActive: false })
        {
            throw new LicenseApiException(401,
                "Tu acceso fue desactivado por el administrador de la cuenta. Contacta a soporte.");
        }

        // Suspendida/vencida se corta aquí, no esperando a que se agote la gracia offline
        // (docs/LICENSING_PLAN.md, "Tokens y gracia offline": "bloqueo en el próximo heartbeat").
        EnsureLicenseUsable(license);

        // Solo actualiza device (IP + last_seen + contador). No escribe audit_log por heartbeat:
        // inundaría la tabla; la frecuencia se ve en Device.SeenCount / LastSeenUtc en admin.
        TouchDevice(device, clientIp);
        await EnsureCurrentPeriodCountersAsync(license, ct);
        await db.SaveChangesAsync(ct);

        var (json, signature) = await BuildSignedBlobAsync(license, device, ct);
        var accessToken = jwt.Issue(license.Id, device.Id);

        return new HeartbeatResponse
        {
            AccessToken = accessToken,
            EntitlementJson = json,
            EntitlementSignatureBase64 = Convert.ToBase64String(signature)
        };
    }

    public async Task<UsageEventResponse> ReportUsageAsync(Guid licenseId, Guid deviceId, UsageEventRequest request, CancellationToken ct)
    {
        var license = await db.Licenses
            .Include(l => l.Plan)
            .FirstOrDefaultAsync(l => l.Id == licenseId, ct);
        if (license is null)
        {
            throw new LicenseApiException(404, "Licencia no encontrada.");
        }

        // Antes solo se llamaba a EnsurePlanServiceAvailable (kill switch del PLAN completo) -- eso
        // ignoraba por completo License.Status/ValidUntil de ESTA licencia puntual. A diferencia de
        // HeartbeatAsync (que sí corta aquí "sin esperar a la gracia offline", ver comentario ahí),
        // una licencia suspendida o vencida podía seguir reportando uso y descontando cuota vía
        // /v1/usage durante toda la vida del JWT ya emitido (hasta 14 días) sin pasar nunca por
        // heartbeat. EnsureLicenseUsable ya incluye la verificación del kill switch del plan.
        EnsureLicenseUsable(license);

        var device = await db.Devices
            .Include(d => d.CompanyUser)
            .FirstOrDefaultAsync(
                d => d.Id == deviceId && d.LicenseId == licenseId && d.Fingerprint == request.DeviceFingerprint, ct);
        if (device is null)
        {
            throw new LicenseApiException(401,
                "Este PC fue desvinculado o ya no está autorizado. Activa de nuevo con tu clave de licencia.");
        }

        // Mismo chequeo que HeartbeatAsync -- ver el comentario ahí para el porqué.
        if (device.CompanyUser is { IsActive: false })
        {
            throw new LicenseApiException(401,
                "Tu acceso fue desactivado por el administrador de la cuenta. Contacta a soporte.");
        }

        var featureCode = request.FeatureCode.Trim().ToLowerInvariant();
        var period = CurrentPeriod();

        // Sin fila en usage_counter, consume_quota devuelve NULL y el cliente descarta el evento.
        await EnsureCurrentPeriodCountersAsync(license, ct);

        var receivedAtUtc = DateTimeOffset.UtcNow;

        // INSERT ... ON CONFLICT (id) DO NOTHING: idempotencia por EventId, sin buscar antes de
        // insertar (docs/LICENSING_PLAN.md, "Dónde vive la lógica: app vs Postgres").
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
            insert into usage_event (id, license_id, device_id, feature_code, quantity, occurred_at_utc, received_at_utc)
            values ({request.EventId}, {licenseId}, {deviceId}, {featureCode}, {request.Quantity}, {request.OccurredAtUtc}, {receivedAtUtc})
            on conflict (id) do nothing
            """, ct);

        if (inserted == 0)
        {
            // Reintento de un evento ya procesado: no se vuelve a consumir cuota.
            var existing = await db.UsageCounters.FirstOrDefaultAsync(
                c => c.LicenseId == licenseId && c.FeatureCode == featureCode && c.Period == period, ct);
            return new UsageEventResponse { Remaining = existing is null ? null : RemainingOf(existing) };
        }

        var remaining = await db.Database
            .SqlQuery<int?>($"select consume_quota({licenseId}, {featureCode}, {request.Quantity}) as \"Value\"")
            .SingleAsync(ct);

        if (remaining is null)
        {
            // Evita eventos huérfanos que bloquean reintentos con el mismo EventId.
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"delete from usage_event where id = {request.EventId}", ct);
            throw new LicenseApiException(503, "No se pudo registrar el uso. Reintenta en unos segundos.");
        }

        return new UsageEventResponse { Remaining = remaining };
    }

    /// <summary>
    /// Libera el dispositivo del JWT (mismo efecto que "Liberar" en admin): el add-in borra su
    /// cache local después. No exige licencia active -- un usuario debe poder desactivar aunque
    /// la licencia esté suspendida.
    /// </summary>
    public async Task<DeactivateResponse> DeactivateAsync(Guid licenseId, Guid deviceId, DeactivateRequest request, CancellationToken ct)
    {
        var device = await db.Devices.FirstOrDefaultAsync(
            d => d.Id == deviceId && d.LicenseId == licenseId && d.Fingerprint == request.DeviceFingerprint, ct);

        if (device is null)
        {
            throw new LicenseApiException(404, "Dispositivo no encontrado o ya liberado.");
        }

        db.Devices.Remove(device);
        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            LicenseId = licenseId,
            Actor = "device",
            Action = "device.deactivate",
            DetailsJson = JsonSerializer.Serialize(new { deviceId, fingerprint = device.Fingerprint, ip = device.LastIp }),
            OccurredAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(ct);

        return new DeactivateResponse { Deactivated = true };
    }

    public async Task<UpdateCheckResponse> CheckUpdateAsync(string? currentVersion, string? revitVersion, CancellationToken ct)
    {
        var stableReleases = await db.Releases
            .Where(r => r.Channel == "stable"
                && (r.Kind == ReleaseKinds.Update || r.Kind == ReleaseKinds.Installer))
            .ToListAsync(ct);

        static (Release Release, SemVersion Version)? LatestValid(
            IEnumerable<Release> releases,
            string kind)
        {
            return releases
                .Where(r => r.Kind == kind)
                .Select(r => SemVersion.TryParse(r.Version, out var version)
                    ? (Release: r, Version: version!)
                    : ((Release Release, SemVersion Version)?)null)
                .Where(candidate => candidate.HasValue)
                .Select(candidate => candidate!.Value)
                .OrderByDescending(candidate => candidate.Version)
                .ThenByDescending(candidate => candidate.Release.PublishedAtUtc)
                .Cast<(Release Release, SemVersion Version)?>()
                .FirstOrDefault();
        }

        // Se prefieren paquetes update; el instalador solo es fallback si no hay un update válido.
        var latest = LatestValid(stableReleases, ReleaseKinds.Update)
            ?? LatestValid(stableReleases, ReleaseKinds.Installer);

        if (latest is null)
        {
            return new UpdateCheckResponse { UpdateAvailable = false };
        }

        var updateAvailable = !SemVersion.TryParse(currentVersion, out var current)
            || latest.Value.Version.IsGreaterThan(current!);
        if (!updateAvailable)
        {
            return new UpdateCheckResponse { UpdateAvailable = false };
        }

        return new UpdateCheckResponse
        {
            UpdateAvailable = true,
            LatestVersion = latest.Value.Version.ToString(),
            DownloadUrl = $"/v1/updates/download/{latest.Value.Release.Id}",
            ReleaseNotes = latest.Value.Release.Notes
        };
    }

    /// <summary>
    /// Devuelve URL firmada temporal (MinIO) para descargar el artefacto. Si MinIO no está
    /// configurado, devuelve la object key cruda (solo útil en desarrollo).
    /// </summary>
    public async Task<string> GetDownloadLocationAsync(Guid releaseId, CancellationToken ct)
    {
        var release = await db.Releases.FindAsync([releaseId], ct)
            ?? throw new LicenseApiException(404, "Release no encontrado.");

        if (artifacts.IsConfigured && !string.IsNullOrWhiteSpace(release.ArtifactLocation))
        {
            return await artifacts.CreatePresignedGetUrlAsync(release.ArtifactLocation, ct);
        }

        return release.ArtifactLocation;
    }

    /// <summary>Último instalador publicado (kind=installer), o null si el catálogo está vacío.</summary>
    public async Task<Release?> TryGetLatestInstallerAsync(CancellationToken ct)
    {
        var installers = await db.Releases
            .Where(r => r.Channel == "stable" && r.Kind == ReleaseKinds.Installer)
            .ToListAsync(ct);

        return installers
            .Select(r => SemVersion.TryParse(r.Version, out var version)
                ? (Release: r, Version: version!)
                : ((Release Release, SemVersion Version)?)null)
            .Where(candidate => candidate.HasValue)
            .Select(candidate => candidate!.Value)
            .OrderByDescending(candidate => candidate.Version)
            .ThenByDescending(candidate => candidate.Release.PublishedAtUtc)
            .Select(candidate => candidate.Release)
            .FirstOrDefault();
    }

    /// <summary>Último instalador publicado (kind=installer) para el enlace público /download.</summary>
    public async Task<Release> GetLatestInstallerAsync(CancellationToken ct) =>
        await TryGetLatestInstallerAsync(ct)
        ?? throw new LicenseApiException(404, "Aún no hay instalador publicado.");

    /// <summary>URL firmada del último instalador.</summary>
    public async Task<string> GetLatestInstallerDownloadUrlAsync(CancellationToken ct)
    {
        var release = await GetLatestInstallerAsync(ct);
        return await GetDownloadLocationAsync(release.Id, ct);
    }

    /// <summary>
    /// Empareja por (CustomerId, Email) sin distinguir mayúsculas -- si la persona ya activó antes
    /// (con otro dispositivo u otra licencia del mismo cliente) reusa el mismo CompanyUser en vez
    /// de duplicar.
    /// </summary>
    private async Task<CompanyUser> FindOrCreateCompanyUserAsync(Guid customerId, string fullName, string email, CancellationToken ct)
    {
        var existing = await db.CompanyUsers.FirstOrDefaultAsync(
            u => u.CustomerId == customerId && u.Email == email, ct);

        if (existing != null)
        {
            if (!existing.IsActive)
            {
                throw new LicenseApiException(403, $"El usuario {email} está desactivado. Contacta a soporte.");
            }

            // Mismo correo en otro PC: actualiza el nombre visible si cambió.
            if (!string.Equals(existing.FullName, fullName, StringComparison.Ordinal))
            {
                existing.FullName = fullName;
            }

            return existing;
        }

        var created = new CompanyUser
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            FullName = fullName,
            Email = email,
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        db.CompanyUsers.Add(created);
        return created;
    }

    private static void EnsureLicenseUsable(License license)
    {
        EnsurePlanServiceAvailable(license.Plan);

        if (license.Status != LicenseStatus.Active)
        {
            throw new LicenseApiException(403, $"Licencia {license.Status.ToString().ToLowerInvariant()}. Contacta a soporte.");
        }

        if (license.ValidUntil < DateTimeOffset.UtcNow)
        {
            throw new LicenseApiException(403, "Licencia vencida. Contacta a soporte para renovarla.");
        }
    }

    /// <summary>
    /// Per-plan emergency kill switch (<see cref="Plan.ServiceSuspended"/>). Independent of
    /// IsActive / License.Status — used for DDoS or temporary Free blocking.
    /// </summary>
    private static void EnsurePlanServiceAvailable(Plan? plan)
    {
        if (plan is { ServiceSuspended: true })
        {
            // 403 so existing add-ins clear cache on heartbeat (~5s) — same path as license Suspended.
            throw new LicenseApiException(403, ServiceSuspendedMessage(plan));
        }
    }

    private static string ServiceSuspendedMessage(Plan plan)
    {
        var name = string.IsNullOrWhiteSpace(plan.DisplayName) ? plan.Code : plan.DisplayName;
        return $"El servicio {name} está temporalmente suspendido. Intenta más tarde.";
    }

    private static DateOnly CurrentPeriod()
    {
        var nowUtc = DateTime.UtcNow;
        return new DateOnly(nowUtc.Year, nowUtc.Month, 1);
    }

    private static int ParseFeatureInt(Dictionary<string, string> effectiveFeatures, string code, int defaultValue) =>
        effectiveFeatures.TryGetValue(code, out var raw) && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : defaultValue;

    // Clamped a 0, nunca negativo: con quota_limit ahora dinámico (se re-sincroniza en cada
    // activate/heartbeat, ver EnsureCurrentPeriodCountersAsync), si un admin baja el tope de un
    // plan a mitad de mes por debajo de lo ya consumido, esta resta puede dar negativo. El resto
    // del sistema (cliente y servidor) asume que "remaining" es o -1 (ilimitado) o un entero >= 0
    // -- un negativo distinto de -1 se interpreta como "ilimitado" en QuotaDisplay.FormatSheetsUsage
    // (chequea remaining < 0), lo que mostraría "ilimitado" justo cuando la cuota está agotada.
    private static int RemainingOf(UsageCounter counter) =>
        counter.QuotaLimit == -1 ? -1 : Math.Max(0, counter.QuotaLimit - counter.Consumed);

    /// <summary>Plan.Features con License.FeatureOverrides encima -- ver comentario en BuildSignedBlobAsync.</summary>
    private static Dictionary<string, string> GetEffectiveFeatures(License license)
    {
        // Misma sensibilidad que PlanFeatureForm.Merge (admin): códigos case-insensitive.
        var effective = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (code, value) in license.Plan!.Features)
        {
            effective[code] = value;
        }

        foreach (var (code, value) in license.FeatureOverrides)
        {
            effective[code] = value;
        }

        return effective;
    }

    private async Task EnsureCurrentPeriodCountersAsync(License license, CancellationToken ct)
    {
        var period = CurrentPeriod();
        var quotaFeatures = GetEffectiveFeatures(license)
            .Where(f => f.Key.StartsWith("quota.", StringComparison.OrdinalIgnoreCase)
                        && !f.Key.EndsWith(".limit", StringComparison.OrdinalIgnoreCase));

        foreach (var (code, rawValue) in quotaFeatures)
        {
            var featureCode = code.Trim().ToLowerInvariant();

            var limit = int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
            var id = Guid.NewGuid();

            // INSERT ... ON CONFLICT DO UPDATE quota_limit (no "do nothing"): el tope debe seguir
            // dinámicamente lo que el admin tenga configurado AHORA MISMO en plan + ajustes de la
            // licencia, no el valor que tenía la primera vez que alguien activó/hizo heartbeat este
            // mes. "consumed" no se toca -- lo ya gastado este mes no se pierde ni se resetea solo
            // porque el admin subió o bajó el tope a mitad de mes.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                insert into usage_counter (id, license_id, feature_code, period, quota_limit, consumed)
                values ({id}, {license.Id}, {featureCode}, {period}, {limit}, {0})
                on conflict (license_id, feature_code, period) do update set quota_limit = excluded.quota_limit
                """, ct);
        }
    }

    private async Task<(string Json, byte[] Signature)> BuildSignedBlobAsync(License license, Device device, CancellationToken ct)
    {
        var period = CurrentPeriod();
        var counters = await db.UsageCounters
            .Where(c => c.LicenseId == license.Id && c.Period == period)
            .ToDictionaryAsync(c => c.FeatureCode, StringComparer.OrdinalIgnoreCase, ct);

        var features = new List<FeatureEntry>();
        foreach (var (code, value) in GetEffectiveFeatures(license))
        {
            var normalized = code.Trim().ToLowerInvariant();
            // Companions de UI; nunca se definen en el plan.
            if (normalized.EndsWith(".limit", StringComparison.Ordinal))
            {
                continue;
            }

            // Para quota.*, el blob lleva el REMANENTE vivo (limit - consumed), no el tope estático
            // del plan -- así el cliente cachea un número que ya refleja lo gastado este mes.
            // Además envía `{code}.limit` para el footer "Usadas X de Y" (UI_FREEMIUM_PLAN.md §3.3).
            if (normalized.StartsWith("quota.", StringComparison.OrdinalIgnoreCase))
            {
                int remaining;
                int limit;
                if (counters.TryGetValue(normalized, out var counter))
                {
                    remaining = RemainingOf(counter);
                    limit = counter.QuotaLimit;
                }
                else
                {
                    limit = int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
                    remaining = limit;
                }

                features.Add(new FeatureEntry
                {
                    Code = normalized,
                    Value = remaining.ToString(CultureInfo.InvariantCulture)
                });
                features.Add(new FeatureEntry
                {
                    Code = normalized + ".limit",
                    Value = limit.ToString(CultureInfo.InvariantCulture)
                });
                continue;
            }

            features.Add(new FeatureEntry
            {
                Code = normalized,
                Value = value
            });
        }

        // No es un feature de plan -- se agrega igual en todo blob, tomado de Admin →
        // Configuración, para que el add-in muestre el correo de soporte que edita el admin en
        // vez de uno fijo en el código (auditoría del sistema: SupportEmailHint nunca se seteaba).
        var appSettings = await db.AppSettings.OrderBy(s => s.Id).FirstOrDefaultAsync(ct);
        features.Add(new FeatureEntry
        {
            Code = "meta.support_email",
            Value = appSettings?.SupportEmail ?? string.Empty
        });

        // El add-in mostraba el Code del plan ("free"/"pro") en vez del nombre visible ("Free"/"Pro")
        // -- auditoría del sistema. PlanCode sigue siendo lo que usa la lógica (es free vs no); esto
        // es solo para lo que ve el usuario.
        features.Add(new FeatureEntry
        {
            Code = "meta.plan_display_name",
            Value = license.Plan!.DisplayName
        });

        var blob = new EntitlementBlob
        {
            LicenseId = license.Id.ToString(),
            PlanCode = license.Plan!.Code,
            Features = features,
            IssuedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
            OfflineUntilUtc = DateTimeOffset.UtcNow.AddDays(7).ToString("O"),
            DeviceId = device.Id.ToString()
        };

        var json = JsonSerializer.Serialize(blob, WireJsonOptions);
        var signature = signer.Sign(Encoding.UTF8.GetBytes(json));
        return (json, signature);
    }
}
