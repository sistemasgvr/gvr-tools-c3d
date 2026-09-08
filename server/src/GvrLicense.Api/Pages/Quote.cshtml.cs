using GvrLicense.Domain.Entities;
using GvrLicense.Domain.Validation;
using GvrLicense.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace GvrLicense.Api.Pages;

/// <summary>
/// Landing pública "/quote" (template <c>_PublicLayout</c>, igual que Download.cshtml): lista los
/// planes activos en vivo desde la BD y guarda el interesado en QuoteRequest para seguimiento
/// manual en Admin → Cotizaciones (mismo modelo "cobro y entrega manual" de todo el sistema, ver
/// RUNBOOK_LICENSING.md).
/// </summary>
[EnableRateLimiting("quote")]
public class QuoteModel(LicenseDbContext db) : PageModel
{
    [BindProperty]
    public QuoteInput Input { get; set; } = new();

    public List<PlanCard> Plans { get; private set; } = [];
    public string? SupportEmail { get; private set; }
    public string? TosUrl { get; private set; }
    public string? PrivacyUrl { get; private set; }
    public bool Submitted { get; private set; }

    public async Task OnGetAsync(string? plan)
    {
        await LoadSharedAsync();
        if (!string.IsNullOrWhiteSpace(plan))
            Input.PlanCode = plan.Trim().ToLowerInvariant();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadSharedAsync();

        // Honeypot: campo oculto por CSS que un visitante real nunca llena; un bot que autocompleta
        // formularios sí. Si viene con algo, se responde como si hubiera funcionado (no delatar el
        // filtro) pero no se guarda nada.
        if (!string.IsNullOrWhiteSpace(Input.Website))
        {
            Submitted = true;
            return Page();
        }

        if (!PersonNameValidator.TryNormalize(Input.FullName, out var fullName, out var nameError))
        {
            ModelState.AddModelError("Input.FullName", nameError);
        }

        if (!EmailValidator.TryNormalize(Input.Email, out var email, out var emailError))
        {
            ModelState.AddModelError("Input.Email", emailError);
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        db.QuoteRequests.Add(new QuoteRequest
        {
            Id = Guid.NewGuid(),
            FullName = fullName,
            Email = email,
            Phone = string.IsNullOrWhiteSpace(Input.Phone) ? null : Input.Phone.Trim(),
            CompanyName = string.IsNullOrWhiteSpace(Input.CompanyName) ? null : Input.CompanyName.Trim(),
            PlanCode = string.IsNullOrWhiteSpace(Input.PlanCode) ? null : Input.PlanCode.Trim().ToLowerInvariant(),
            Message = string.IsNullOrWhiteSpace(Input.Message) ? null : Input.Message.Trim(),
            Status = QuoteRequestStatus.New,
            SourceIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        Submitted = true;
        Input = new QuoteInput();
        return Page();
    }

    private async Task LoadSharedAsync()
    {
        var settings = await db.AppSettings.OrderBy(s => s.Id).FirstOrDefaultAsync();
        SupportEmail = settings?.SupportEmail;
        TosUrl = settings?.TermsOfServiceUrl;
        PrivacyUrl = settings?.PrivacyPolicyUrl;

        var plans = await db.Plans
            .Where(p => p.IsActive)
            .OrderBy(p => p.Code)
            .ToListAsync();

        Plans = plans
            .Select(p => new PlanCard(p.Code, p.DisplayName, BuildFeatureSummary(p.Features)))
            .ToList();
    }

    private static List<FeatureLine> BuildFeatureSummary(Dictionary<string, string> features)
    {
        // Un ícono por TIPO de feature (formato/cuota/lote/asientos), no por plan -- así sigue
        // siendo 100% dinámico: agregar un plan nuevo o cambiar sus valores en Admin no requiere
        // tocar esta página, y ningún ícono queda ligado al nombre/código de un plan en particular.
        var lines = new List<FeatureLine>();

        var formats = new List<string>();
        if (IsTruthy(features, "format.pdf")) formats.Add("PDF");
        if (IsTruthy(features, "format.dwg")) formats.Add("DWG");
        if (formats.Count > 0)
        {
            lines.Add(new FeatureLine("bi-file-earmark-richtext", "Formatos: " + string.Join(" + ", formats)));
        }

        if (TryGetInt(features, "quota.sheets_per_month", out var quota))
        {
            lines.Add(new FeatureLine("bi-graph-up-arrow",
                quota < 0 ? "Láminas por mes: ilimitadas" : $"Láminas por mes: {quota}"));
        }

        if (TryGetInt(features, "limit.sheets_per_batch", out var batch) && batch > 0)
        {
            lines.Add(new FeatureLine("bi-collection", $"Hasta {batch} láminas por lote"));
        }

        if (TryGetInt(features, "seat.max_devices_per_user", out var seats))
        {
            lines.Add(new FeatureLine("bi-laptop",
                seats < 0 ? "Dispositivos por usuario: ilimitados" : $"Dispositivos por usuario: {seats}"));
        }

        return lines;
    }

    private static bool IsTruthy(Dictionary<string, string> features, string code) =>
        features.TryGetValue(code, out var value)
        && !string.IsNullOrWhiteSpace(value)
        && !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)
        && value != "0";

    private static bool TryGetInt(Dictionary<string, string> features, string code, out int value)
    {
        value = 0;
        return features.TryGetValue(code, out var raw) && int.TryParse(raw, out value);
    }

    public sealed record PlanCard(string Code, string DisplayName, List<FeatureLine> FeatureSummary);

    public sealed record FeatureLine(string Icon, string Text);

    public sealed class QuoteInput
    {
        public string? FullName { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? CompanyName { get; set; }
        public string? PlanCode { get; set; }
        public string? Message { get; set; }

        /// <summary>Honeypot -- debe llegar siempre vacío. Nombre genérico a propósito para no delatar el propósito a un bot.</summary>
        public string? Website { get; set; }
    }
}
