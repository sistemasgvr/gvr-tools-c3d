using GvrLicense.Domain.Validation;
using GvrLicense.Infrastructure;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GvrLicense.Api.Pages.Admin.Customers;

public class MembersModel(LicenseDbContext db, IAntiforgery antiforgery) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid CustomerId { get; set; }

    public string CompanyName { get; private set; } = string.Empty;
    public List<MemberRow> Rows { get; private set; } = [];

    /// <summary>Tabulator genera el botón Activar/Desactivar por fila como HTML crudo, necesita el token a mano.</summary>
    public string AntiForgeryToken { get; private set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync()
    {
        var customer = await db.Customers.FindAsync(CustomerId);
        if (customer is null)
        {
            return NotFound();
        }

        CompanyName = customer.CompanyName;
        AntiForgeryToken = antiforgery.GetAndStoreTokens(HttpContext).RequestToken!;

        Rows = await db.CompanyUsers
            .Where(u => u.CustomerId == CustomerId)
            .OrderBy(u => u.FullName)
            .Select(u => new MemberRow(
                u.Id, u.FullName, u.Email, u.IsActive, u.CreatedAtUtc,
                u.Devices.Select(d => d.DisplayName ?? d.Fingerprint).ToList()))
            .ToListAsync();

        return Page();
    }

    /// <summary>
    /// Desactivar no libera el seat ni borra los dispositivos ya activados -- solo bloquea futuras
    /// activaciones/heartbeats de esa persona (LicenseEngine.FindOrCreateCompanyUserAsync).
    /// </summary>
    public async Task<IActionResult> OnPostToggleAsync(Guid userId)
    {
        var user = await db.CompanyUsers.FindAsync(userId);
        if (user != null)
        {
            user.IsActive = !user.IsActive;
            await db.SaveChangesAsync();
            return RedirectToPage(new { customerId = user.CustomerId });
        }

        return RedirectToPage(new { customerId = CustomerId });
    }

    /// <summary>Corrige un nombre/correo mal tipeado -- el correo sigue siendo único por cliente (índice en LicenseDbContext).</summary>
    public async Task<IActionResult> OnPostEditAsync(Guid userId, string fullName, string email)
    {
        var user = await db.CompanyUsers.FindAsync(userId);
        if (user is null)
        {
            return RedirectToPage(new { customerId = CustomerId });
        }

        if (!PersonNameValidator.TryNormalize(fullName, out var normalizedName, out var nameError))
        {
            TempData["MemberError"] = nameError;
            return RedirectToPage(new { customerId = user.CustomerId });
        }

        if (!EmailValidator.TryNormalize(email, out var normalizedEmail, out var emailError))
        {
            TempData["MemberError"] = emailError;
            return RedirectToPage(new { customerId = user.CustomerId });
        }

        user.FullName = normalizedName;
        user.Email = normalizedEmail;
        await db.SaveChangesAsync();
        return RedirectToPage(new { customerId = user.CustomerId });
    }

    public sealed record MemberRow(Guid Id, string FullName, string Email, bool IsActive, DateTimeOffset CreatedAtUtc, List<string> Devices);
}
