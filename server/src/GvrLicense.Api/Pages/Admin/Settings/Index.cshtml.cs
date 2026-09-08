using GvrLicense.Domain.Entities;
using GvrLicense.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GvrLicense.Api.Pages.Admin.Settings;

public class IndexModel(LicenseDbContext db) : PageModel
{
    [BindProperty]
    public SettingsInput Input { get; set; } = new();

    public async Task OnGetAsync()
    {
        var row = await db.AppSettings.OrderBy(s => s.Id).FirstOrDefaultAsync();
        if (row is null)
        {
            return;
        }

        Input = new SettingsInput
        {
            SupportEmail = row.SupportEmail,
            TermsOfServiceUrl = row.TermsOfServiceUrl,
            PrivacyPolicyUrl = row.PrivacyPolicyUrl
        };
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var row = await db.AppSettings.OrderBy(s => s.Id).FirstOrDefaultAsync();
        if (row is null)
        {
            row = new AppSettings { Id = Guid.NewGuid() };
            db.AppSettings.Add(row);
        }

        row.SupportEmail = Input.SupportEmail?.Trim() ?? string.Empty;
        row.TermsOfServiceUrl = Input.TermsOfServiceUrl?.Trim() ?? string.Empty;
        row.PrivacyPolicyUrl = Input.PrivacyPolicyUrl?.Trim() ?? string.Empty;
        await db.SaveChangesAsync();

        TempData["Saved"] = true;
        return RedirectToPage();
    }

    public sealed class SettingsInput
    {
        public string? SupportEmail { get; set; }
        public string? TermsOfServiceUrl { get; set; }
        public string? PrivacyPolicyUrl { get; set; }
    }
}
