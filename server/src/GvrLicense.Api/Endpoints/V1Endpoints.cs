using System.Security.Claims;
using GvrLicense.Api.Services;
using GvrLicense.Contracts;
using GvrLicense.Infrastructure.Signing;

namespace GvrLicense.Api.Endpoints;

/// <summary>Los cinco endpoints de docs/LICENSING_PLAN.md, "API mínima v1", documentados para Swagger.</summary>
public static class V1Endpoints
{
    /// <summary>Nombre del esquema de autorización para heartbeat/usage -- ver Program.cs (AddJwtBearer + AddPolicy).</summary>
    public const string BearerPolicy = "V1Bearer";

    /// <summary>Nombre de la política de rate limiting de activate-free -- ver Program.cs (AddRateLimiter).</summary>
    public const string ActivateFreeRateLimitPolicy = "activate-free";

    /// <summary>Nombre de la política de rate limiting de activate (key de pago) -- ver Program.cs (AddRateLimiter).</summary>
    public const string ActivateRateLimitPolicy = "activate";

    public static IEndpointRouteBuilder MapV1Endpoints(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/v1").WithTags("v1 (add-in)");

        v1.MapPost("/activate", async (ActivateRequest request, HttpContext http, LicenseEngine engine, ILogger<Program> log, CancellationToken ct) =>
                await RunAsync(log, "activate", () => engine.ActivateAsync(request, ClientIp(http), ct)))
            .AllowAnonymous()
            .RequireRateLimiting(ActivateRateLimitPolicy)
            .WithSummary("Activa una license key en este dispositivo")
            .WithDescription("Valida la key, crea/renueva el seat (node-locked por fingerprint) y devuelve el blob de entitlements firmado + un JWT (AccessToken) para heartbeat/usage.")
            .Produces<ActivateResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        v1.MapPost("/activate-free", async (ActivateFreeRequest request, HttpContext http, LicenseEngine engine, ILogger<Program> log, CancellationToken ct) =>
                await RunAsync(log, "activate-free", () => engine.ActivateFreeAsync(request, ClientIp(http), ct)))
            .AllowAnonymous()
            .RequireRateLimiting(ActivateFreeRateLimitPolicy)
            .WithSummary("Registra este dispositivo en el plan free (sin license key)")
            .WithDescription("UI_FREEMIUM_PLAN.md §4.1. Mismo fingerprint siempre resuelve a la misma licencia (free o de pago); no crea una free nueva por reinstalación.")
            .Produces<ActivateResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        v1.MapPost("/heartbeat", async (HeartbeatRequest request, HttpContext http, ClaimsPrincipal user, LicenseEngine engine, ILogger<Program> log, CancellationToken ct) =>
                await RunAsync(log, "heartbeat", () =>
                {
                    var (licenseId, deviceId) = RequireClaims(user);
                    return engine.HeartbeatAsync(licenseId, deviceId, request, ClientIp(http), ct);
                }))
            .RequireAuthorization(BearerPolicy)
            .WithSummary("Renueva la gracia offline y refresca entitlements/cuotas")
            .WithDescription("Requiere 'Authorization: Bearer {AccessToken}' (JWT emitido por /v1/activate). Si la licencia fue suspendida en admin, corta aquí en vez de esperar a que se agote la gracia de 7 días.")
            .Produces<HeartbeatResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        v1.MapPost("/usage", async (UsageEventRequest request, ClaimsPrincipal user, LicenseEngine engine, ILogger<Program> log, CancellationToken ct) =>
                await RunAsync(log, "usage", () =>
                {
                    var (licenseId, deviceId) = RequireClaims(user);
                    return engine.ReportUsageAsync(licenseId, deviceId, request, ct);
                }))
            .RequireAuthorization(BearerPolicy)
            .WithSummary("Reporta consumo de una lámina exportada con éxito")
            .WithDescription("Requiere 'Authorization: Bearer {AccessToken}'. Idempotente por EventId: un reintento de red tras una caída offline no duplica el consumo.")
            .Produces<UsageEventResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        v1.MapPost("/deactivate", async (DeactivateRequest request, ClaimsPrincipal user, LicenseEngine engine, ILogger<Program> log, CancellationToken ct) =>
                await RunAsync(log, "deactivate", () =>
                {
                    var (licenseId, deviceId) = RequireClaims(user);
                    return engine.DeactivateAsync(licenseId, deviceId, request, ct);
                }))
            .RequireAuthorization(BearerPolicy)
            .WithSummary("Libera este PC (Desactivar este PC en el add-in)")
            .WithDescription("Requiere 'Authorization: Bearer {AccessToken}'. Borra el device del seat para que otra máquina o el mismo usuario pueda activar de nuevo.")
            .Produces<DeactivateResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        v1.MapGet("/updates/check", async (string? version, string? revit, LicenseEngine engine, ILogger<Program> log, CancellationToken ct) =>
                await RunAsync(log, "updates/check", () => engine.CheckUpdateAsync(version, revit, ct)))
            .AllowAnonymous()
            .WithSummary("Consulta si hay una versión más nueva en el canal stable")
            .Produces<UpdateCheckResponse>();

        v1.MapGet("/updates/download/{id:guid}", async (Guid id, LicenseEngine engine, ILogger<Program> log, CancellationToken ct) =>
                await RunAsync(log, "updates/download", async () => new UpdateDownloadResponse
                {
                    Location = await engine.GetDownloadLocationAsync(id, ct)
                }))
            .AllowAnonymous()
            .WithSummary("Ubicación del artefacto de un release")
            .WithDescription("Devuelve una URL firmada temporal (MinIO) para descargar el artefacto.")
            .Produces<UpdateDownloadResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    /// <summary>
    /// Preferencia X-Forwarded-For (primer hop) detrás de Coolify/nginx; si no, RemoteIpAddress.
    /// Misma fuente para activate / heartbeat / rate limit partition cuando se usa desde aquí.
    /// </summary>
    private static string? ClientIp(HttpContext http)
    {
        var forwarded = http.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            var first = forwarded.Split(',', 2)[0].Trim();
            if (first.Length > 0)
            {
                return first;
            }
        }

        return http.Connection.RemoteIpAddress?.ToString();
    }

    /// <summary>
    /// El middleware AddJwtBearer ya validó firma/emisor/audiencia/expiración antes de llegar aquí
    /// (RequireAuthorization corta con 401 si el token falta o es inválido); esto solo lee los
    /// claims que JwtSessionTokenService.Issue puso en el token.
    /// </summary>
    private static (Guid LicenseId, Guid DeviceId) RequireClaims(ClaimsPrincipal user)
    {
        var licenseId = user.FindFirstValue(JwtSessionTokenService.LicenseIdClaim);
        var deviceId = user.FindFirstValue(JwtSessionTokenService.DeviceIdClaim);

        if (!Guid.TryParse(licenseId, out var lid) || !Guid.TryParse(deviceId, out var did))
        {
            throw new LicenseApiException(401, "Token sin los claims esperados.");
        }

        return (lid, did);
    }

    /// <summary>
    /// El add-in siempre espera un ProblemDetails JSON en cualquier falla, no una página HTML de
    /// error ni una respuesta vacía -- por eso este catch-all además del catch específico de
    /// LicenseApiException: sin él, una NpgsqlException transitoria o un timeout de MinIO se
    /// atraparían recién en el manejador global de Program.cs, que sí responde JSON pero sin el
    /// nombre del endpoint en el log. Atrapar aquí también significa que el manejador global NUNCA
    /// ve estas excepciones (quedan resueltas en este catch) -- por eso el log tiene que pasar
    /// justo aquí, no se puede asumir que "ya quedó registrado más arriba".
    /// </summary>
    private static async Task<IResult> RunAsync<T>(ILogger log, string endpoint, Func<Task<T>> action)
    {
        try
        {
            return Results.Ok(await action());
        }
        catch (LicenseApiException ex)
        {
            return Results.Problem(ex.Message, statusCode: ex.StatusCode);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Fallo no controlado en /v1/{Endpoint}", endpoint);

            // Nunca se expone el mensaje real de la excepción al cliente (podría filtrar detalles
            // de infraestructura); el detalle completo ya quedó en el log de arriba.
            return Results.Problem(
                "No se pudo completar la operación. Intenta de nuevo en unos minutos.",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
