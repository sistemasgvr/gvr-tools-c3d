using GvrLicense.Domain.Entities;
using GvrLicense.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GvrLicense.Api.Pages.Admin.Customers;

public class CreateModel(LicenseDbContext db) : PageModel
{
    [BindProperty]
    public CustomerInput Input { get; set; } = new();

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var created = new Customer
        {
            Id = Guid.NewGuid(),
            CompanyName = Input.CompanyName,
            ContactName = Input.ContactName,
            ContactEmail = Input.ContactEmail,
            PaymentNotes = Input.PaymentNotes,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        db.Customers.Add(created);
        await db.SaveChangesAsync();

        return RedirectToPage("/Admin/Licenses/Create", new { customerId = created.Id });
    }

    public sealed class CustomerInput
    {
        public string CompanyName { get; set; } = string.Empty;
        public string ContactName { get; set; } = string.Empty;
        public string ContactEmail { get; set; } = string.Empty;
        public string? PaymentNotes { get; set; }
    }
}
