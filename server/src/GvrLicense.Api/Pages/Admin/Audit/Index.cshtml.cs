using System.Text.Json;
using GvrLicense.Domain.Audit;
using GvrLicense.Domain.LicenseKeys;
using GvrLicense.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GvrLicense.Api.Pages.Admin.Audit;

public class IndexModel(LicenseDbContext db) : PageModel
{
    /// <summary>UI_FREEMIUM_PLAN.md §4.2: "sin ML en v1" -- un umbral fijo y explicable basta para que soporte sepa qué mirar.</summary>
    private const int RiskyIpAttemptThreshold = 3;

    /// <summary>Filtro libre: IP, fingerprint, actor, acción o texto en details.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    public List<AuditRow> Rows { get; private set; } = [];

    public List<RiskyIpRow> RiskyIps { get; private set; } = [];
    public List<SharedFingerprintRow> SharedFingerprints { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var rows = await db.AuditLogs
            .OrderByDescending(a => a.OccurredAtUtc)
            .Take(500)
            .Select(a => new { a.Id, a.Actor, a.Action, a.DetailsJson, a.OccurredAtUtc, a.LicenseId })
            .ToListAsync();

        var mapped = rows
            .Select(a => new AuditRow(
                a.Id,
                a.Actor,
                a.Action,
                AuditActionDescriber.Describe(a.Action, a.DetailsJson),
                AuditDetailsFormatter.Summarize(a.DetailsJson),
                AuditDetailsFormatter.TryGetIp(a.DetailsJson),
                AuditDetailsFormatter.TryGetFingerprint(a.DetailsJson),
                a.DetailsJson,
                a.OccurredAtUtc,
                a.LicenseId))
            .ToList();

        if (!string.IsNullOrWhiteSpace(Q))
        {
            var needle = Q.Trim();
            mapped = mapped
                .Where(r => ContainsIgnoreCase(r.Actor, needle)
                            || ContainsIgnoreCase(r.Action, needle)
                            || ContainsIgnoreCase(r.ActionLabel, needle)
                            || ContainsIgnoreCase(r.DetailSummary, needle)
                            || ContainsIgnoreCase(r.Ip, needle)
                            || ContainsIgnoreCase(r.Fingerprint, needle)
                            || ContainsIgnoreCase(r.DetailsJson, needle))
                .ToList();
        }

        Rows = mapped;

        // Señales de riesgo (§4.2/§4.3): calculadas sobre las mismas 500 filas ya cargadas arriba
        // -- no hace falta una consulta aparte, y "recientes" queda acotado de forma natural por el
        // mismo Take(500) que ya limita la tabla de auditoría.
        RiskyIps = rows
            .Where(a => a.Action is "license.activate_free" or "security.activate_free_denied")
            .Select(a => (Ip: TryGetJsonString(a.DetailsJson, "ip"), Denied: a.Action == "security.activate_free_denied", a.OccurredAtUtc))
            .Where(x => !string.IsNullOrWhiteSpace(x.Ip))
            .GroupBy(x => x.Ip!)
            .Select(g => new RiskyIpRow(g.Key, g.Count(), g.Count(x => x.Denied), g.Max(x => x.OccurredAtUtc)))
            .Where(r => r.AttemptCount >= RiskyIpAttemptThreshold || r.DeniedCount > 0)
            .OrderByDescending(r => r.LastSeenUtc)
            .ToList();

        // Mismo fingerprint en más de una licencia: no es necesariamente abuso (alguien probó una
        // trial y luego activó la de pago sin desactivar la anterior), pero vale la pena que soporte
        // lo vea -- ActivateAsync (con key) solo revisa dispositivos DENTRO de esa licencia, así que
        // el mismo fingerprint sí puede terminar repetido entre licencias distintas.
        var deviceFingerprints = await db.Devices
            .Select(d => new { d.Fingerprint, d.LicenseId })
            .ToListAsync();

        var groupedByFingerprint = deviceFingerprints
            .GroupBy(d => d.Fingerprint)
            .Where(g => g.Select(d => d.LicenseId).Distinct().Count() > 1)
            .ToList();

        if (groupedByFingerprint.Count > 0)
        {
            var licenseIds = groupedByFingerprint
                .SelectMany(g => g.Select(d => d.LicenseId))
                .Distinct()
                .ToList();

            var licenseKeysById = await db.Licenses
                .Where(l => licenseIds.Contains(l.Id))
                .Select(l => new { l.Id, l.Key })
                .ToDictionaryAsync(l => l.Id, l => l.Key);

            SharedFingerprints = groupedByFingerprint
                .Select(g => new SharedFingerprintRow(
                    g.Key,
                    g.Select(d => d.LicenseId).Distinct().Count(),
                    g.Select(d => d.LicenseId).Distinct()
                        .Select(id => licenseKeysById.TryGetValue(id, out var key) ? LicenseKeyGenerator.FormatForDisplay(key) : "—")
                        .ToList()))
                .OrderByDescending(r => r.LicenseCount)
                .ToList();
        }
    }

    private static bool ContainsIgnoreCase(string? haystack, string needle) =>
        !string.IsNullOrEmpty(haystack)
        && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static string? TryGetJsonString(string? detailsJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(detailsJson))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(detailsJson);
            return doc.RootElement.TryGetProperty(propertyName, out var element) && element.ValueKind == JsonValueKind.String
                ? element.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public sealed record AuditRow(
        Guid Id,
        string Actor,
        string Action,
        string ActionLabel,
        string DetailSummary,
        string? Ip,
        string? Fingerprint,
        string? DetailsJson,
        DateTimeOffset OccurredAtUtc,
        Guid? LicenseId);

    public sealed record RiskyIpRow(string Ip, int AttemptCount, int DeniedCount, DateTimeOffset LastSeenUtc);

    public sealed record SharedFingerprintRow(string Fingerprint, int LicenseCount, List<string> LicenseKeys);
}
