using GvrLicense.Domain.Entities;
using GvrLicense.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GvrLicense.Api.Pages.Admin.Quotes;

public class DetailsModel(LicenseDbContext db) : PageModel
{
    public QuoteRequest? Quote { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        Quote = await db.QuoteRequests.FindAsync(id);
        return Quote is null ? NotFound() : Page();
    }

    public async Task<IActionResult> OnPostSetStatusAsync(Guid id, QuoteRequestStatus status)
    {
        var quote = await db.QuoteRequests.FindAsync(id);
        if (quote != null)
        {
            quote.Status = status;
            await db.SaveChangesAsync();
        }

        return RedirectToPage(new { id });
    }
}
