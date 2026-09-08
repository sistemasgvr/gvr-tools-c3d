using GvrLicense.Domain.Entities;
using GvrLicense.Infrastructure;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GvrLicense.Api.Pages.Admin.Quotes;

public class IndexModel(LicenseDbContext db, IAntiforgery antiforgery) : PageModel
{
    public List<QuoteRow> Rows { get; private set; } = [];
    public int NewCount { get; private set; }

    /// <summary>Tabulator genera los botones de estado por fila como HTML crudo, necesita el token a mano.</summary>
    public string AntiForgeryToken { get; private set; } = string.Empty;

    public async Task OnGetAsync()
    {
        AntiForgeryToken = antiforgery.GetAndStoreTokens(HttpContext).RequestToken!;

        var quotes = await db.QuoteRequests
            .OrderByDescending(q => q.CreatedAtUtc)
            .ToListAsync();

        Rows = quotes
            .Select(q => new QuoteRow(
                q.Id, q.FullName, q.Email, q.Phone, q.CompanyName, q.PlanCode, q.Message,
                q.Status.ToString(), q.CreatedAtUtc))
            .ToList();

        NewCount = quotes.Count(q => q.Status == QuoteRequestStatus.New);
    }

    public async Task<IActionResult> OnPostSetStatusAsync(Guid id, QuoteRequestStatus status)
    {
        var quote = await db.QuoteRequests.FindAsync(id);
        if (quote != null)
        {
            quote.Status = status;
            await db.SaveChangesAsync();
        }

        return RedirectToPage();
    }

    public sealed record QuoteRow(
        Guid Id, string FullName, string Email, string? Phone, string? CompanyName,
        string? PlanCode, string? Message, string Status, DateTimeOffset CreatedAtUtc);
}
