using GvrLicense.Infrastructure;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GvrLicense.Api.Pages.Admin.Customers;

public class IndexModel(LicenseDbContext db, IAntiforgery antiforgery) : PageModel
{
    public List<CustomerRow> Rows { get; private set; } = [];

    /// <summary>Tabulator genera los botones Editar/Activar/Desactivar por fila como HTML crudo, necesita el token a mano.</summary>
    public string AntiForgeryToken { get; private set; } = string.Empty;

    public async Task OnGetAsync()
    {
        AntiForgeryToken = antiforgery.GetAndStoreTokens(HttpContext).RequestToken!;

        Rows = await db.Customers
            .OrderByDescending(c => c.CreatedAtUtc)
            .Select(c => new CustomerRow(
                c.Id, c.CompanyName, c.ContactName, c.ContactEmail, c.PaymentNotes,
                c.IsActive, c.Licenses.Count, c.CreatedAtUtc))
            .ToListAsync();
    }

    /// <summary>Desactivar es solo un flag administrativo -- no toca las licencias del cliente. Ver Entities/Customer.cs.</summary>
    public async Task<IActionResult> OnPostToggleAsync(Guid customerId)
    {
        var customer = await db.Customers.FindAsync(customerId);
        if (customer != null)
        {
            customer.IsActive = !customer.IsActive;
            await db.SaveChangesAsync();
        }

        return RedirectToPage();
    }

    public sealed record CustomerRow(
        Guid Id, string CompanyName, string ContactName, string ContactEmail, string? PaymentNotes,
        bool IsActive, int LicenseCount, DateTimeOffset CreatedAtUtc);
}
