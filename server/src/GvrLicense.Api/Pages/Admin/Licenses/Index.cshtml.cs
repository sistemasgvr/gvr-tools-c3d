using GvrLicense.Domain.Entities;
using GvrLicense.Domain.LicenseKeys;
using GvrLicense.Infrastructure;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GvrLicense.Api.Pages.Admin.Licenses;

public class IndexModel(LicenseDbContext db, IAntiforgery antiforgery) : PageModel
{
    public List<LicenseRow> Rows { get; private set; } = [];

    /// <summary>Para el <select> del modal "Nueva licencia" -- el POST real lo procesa Licenses/Create.</summary>
    public List<SelectListItem> CustomerOptions { get; private set; } = [];
    public List<SelectListItem> PlanOptions { get; private set; } = [];

    /// <summary>
    /// Tabulator genera el botón Suspender/Reactivar por fila como HTML crudo (no hay `<form>` de
    /// Razor por fila), así que necesita el token a mano para poder incluirlo en cada form generado.
    /// </summary>
    public string AntiForgeryToken { get; private set; } = string.Empty;

    public async Task OnGetAsync()
    {
        AntiForgeryToken = antiforgery.GetAndStoreTokens(HttpContext).RequestToken!;

        var rows = await db.Licenses
            .Include(l => l.Customer)
            .Include(l => l.Plan)
            .Include(l => l.Devices)
            .OrderByDescending(l => l.CreatedAtUtc)
            .Select(l => new
            {
                l.Id,
                l.Key,
                CustomerName = l.Customer!.CompanyName,
                PlanCode = l.Plan!.Code,
                l.Status,
                l.ValidUntil,
                UserCount = l.Devices.Select(d => d.CompanyUserId).Distinct().Count(),
                l.MaxUsers
            })
            .ToListAsync();

        Rows = rows
            .Select(l => new LicenseRow(
                l.Id,
                l.Key,
                LicenseKeyGenerator.FormatForDisplay(l.Key),
                l.CustomerName,
                l.PlanCode,
                l.Status,
                l.ValidUntil,
                l.UserCount,
                l.MaxUsers))
            .ToList();

        CustomerOptions = await db.Customers
            .Where(c => c.IsActive)
            .OrderBy(c => c.CompanyName)
            .Select(c => new SelectListItem(c.CompanyName, c.Id.ToString()))
            .ToListAsync();

        PlanOptions = await db.Plans
            .Where(p => p.IsActive)
            .OrderBy(p => p.Code)
            .Select(p => new SelectListItem(p.DisplayName, p.Id.ToString()))
            .ToListAsync();
    }

    /// <summary>
    /// El trigger de auditoría (Sql/AuditLogTrigger.sql) ya registra el cambio de status solo con
    /// hacer el UPDATE, así que este handler no necesita escribir auditoría a mano.
    /// </summary>
    public async Task<IActionResult> OnPostToggleAsync(Guid licenseId)
    {
        var license = await db.Licenses.FindAsync(licenseId);
        if (license != null)
        {
            license.Status = license.Status == LicenseStatus.Active ? LicenseStatus.Suspended : LicenseStatus.Active;
            await db.SetAuditActorAsync(User.Identity?.Name ?? "admin");
            await db.SaveChangesAsync();
        }

        return RedirectToPage();
    }

    public sealed record LicenseRow(
        Guid Id, string Key, string DisplayKey, string CustomerName, string PlanCode, LicenseStatus Status,
        DateTimeOffset ValidUntil, int UserCount, int MaxUsers);
}
