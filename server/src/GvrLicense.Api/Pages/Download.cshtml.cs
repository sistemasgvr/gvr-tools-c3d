using GvrLicense.Api.Services;
using GvrLicense.Infrastructure;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GvrLicense.Api.Pages;

/// <summary>
/// Landing pública en "/download" (template <c>_PublicLayout</c>).
/// La descarga real del .exe sigue en GET /download/file (redirect MinIO firmado).
/// </summary>
public class DownloadModel(LicenseDbContext db, LicenseEngine engine) : PageModel
{
    public bool HasInstaller { get; private set; }
    public string? LatestVersion { get; private set; }
    public string? LatestNotes { get; private set; }
    public DateTimeOffset? LatestPublishedAtUtc { get; private set; }
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

        HasInstaller = true;
        LatestVersion = latest.Version;
        LatestNotes = latest.Notes;
        LatestPublishedAtUtc = latest.PublishedAtUtc;
    }
}
