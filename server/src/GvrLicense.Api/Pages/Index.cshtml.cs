using GvrLicense.Api.Services;
using GvrLicense.Infrastructure;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GvrLicense.Api.Pages;

/// <summary>
/// Landing pública en "/": CTA de descarga + pasos. Las versiones las publica el admin
/// en /Admin/Releases; el cliente solo ve la última (sin catálogo).
/// </summary>
public class IndexModel(LicenseDbContext db, LicenseEngine engine) : PageModel
{
    public ReleaseCard? LatestInstaller { get; private set; }
    public string? SupportEmail { get; private set; }
    public string? TosUrl { get; private set; }
    public string? PrivacyUrl { get; private set; }

    public async Task OnGetAsync()
    {
        var settings = await db.AppSettings.OrderBy(s => s.Id).FirstOrDefaultAsync();
        SupportEmail = settings?.SupportEmail;
        TosUrl = settings?.TermsOfServiceUrl;
        PrivacyUrl = settings?.PrivacyPolicyUrl;

        var latest = await engine.TryGetLatestInstallerAsync(HttpContext.RequestAborted);
        if (latest is null)
            return;

        LatestInstaller = new ReleaseCard(
            latest.Id,
            latest.Version,
            latest.Notes,
            latest.PublishedAtUtc);
    }

    public sealed record ReleaseCard(Guid Id, string Version, string? Notes, DateTimeOffset PublishedAtUtc);
}
