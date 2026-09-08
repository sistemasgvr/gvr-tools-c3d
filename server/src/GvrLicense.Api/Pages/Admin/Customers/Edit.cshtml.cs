using GvrLicense.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GvrLicense.Api.Pages.Admin.Customers;

public class EditModel(LicenseDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    [BindProperty]
    public CustomerInput Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        var customer = await db.Customers.FindAsync(Id);
        if (customer is null)
        {
            return NotFound();
        }

        Input = new CustomerInput
        {
            CompanyName = customer.CompanyName,
            ContactName = customer.ContactName,
            ContactEmail = customer.ContactEmail,
            PaymentNotes = customer.PaymentNotes
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var customer = await db.Customers.FindAsync(Id);
        if (customer is null)
        {
            return NotFound();
        }

        customer.CompanyName = Input.CompanyName.Trim();
        customer.ContactName = Input.ContactName.Trim();
        customer.ContactEmail = Input.ContactEmail.Trim();
        customer.PaymentNotes = Input.PaymentNotes;
        await db.SaveChangesAsync();

        TempData["Saved"] = true;
        return RedirectToPage(new { id = Id });
    }

    public sealed class CustomerInput
    {
        public string CompanyName { get; set; } = string.Empty;
        public string ContactName { get; set; } = string.Empty;
        public string ContactEmail { get; set; } = string.Empty;
        public string? PaymentNotes { get; set; }
    }
}
