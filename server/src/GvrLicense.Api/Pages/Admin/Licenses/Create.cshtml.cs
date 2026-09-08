using GvrLicense.Domain.Entities;
using GvrLicense.Domain.LicenseKeys;
using GvrLicense.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GvrLicense.Api.Pages.Admin.Licenses;

public class CreateModel(LicenseDbContext db) : PageModel
{
    [BindProperty]
    public LicenseInput Input { get; set; } = new();

    public List<SelectListItem> CustomerOptions { get; private set; } = [];
    public List<SelectListItem> PlanOptions { get; private set; } = [];

    /// <summary>Se muestra una sola vez tras crear -- coherente con "Entrega de la key: manual" del plan.</summary>
    public string? CreatedKey { get; private set; }

    public async Task OnGetAsync(Guid? customerId = null)
    {
        await LoadOptionsAsync();

        if (customerId is Guid id &&
            CustomerOptions.Any(o => o.Value == id.ToString()))
        {
            Input.CustomerId = id;
        }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadOptionsAsync();

        // Reintenta si por rarísima casualidad la key generada ya existe (constraint única en `license.key`).
        string key;
        for (var attempt = 0; ; attempt++)
        {
            key = LicenseKeyGenerator.Generate();
            var exists = await db.Licenses.AnyAsync(l => l.Key == key);
            if (!exists)
            {
                break;
            }
            if (attempt >= 5)
            {
                ModelState.AddModelError(string.Empty, "No se pudo generar una key única, intenta de nuevo.");
                return Page();
            }
        }

        db.Licenses.Add(new License
        {
            Id = Guid.NewGuid(),
            Key = key,
            CustomerId = Input.CustomerId,
            PlanId = Input.PlanId,
            Status = LicenseStatus.Active,
            ValidUntil = new DateTimeOffset(Input.ValidUntil, TimeOnly.MaxValue, TimeSpan.Zero),
            MaxUsers = Input.MaxUsers,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        CreatedKey = key;
        return Page();
    }

    private async Task LoadOptionsAsync()
    {
        CustomerOptions = await db.Customers
            .Where(c => c.IsActive)
            .OrderBy(c => c.CompanyName)
            .Select(c => new SelectListItem(c.CompanyName, c.Id.ToString()))
            .ToListAsync();

        // Solo planes activos: uno descontinuado no debe poder asignarse a licencias nuevas,
        // aunque las licencias existentes que ya lo usan lo sigan usando igual (Plans/Index.cshtml.cs).
        PlanOptions = await db.Plans
            .Where(p => p.IsActive)
            .OrderBy(p => p.Code)
            .Select(p => new SelectListItem(p.DisplayName, p.Id.ToString()))
            .ToListAsync();
    }

    public sealed class LicenseInput
    {
        public Guid CustomerId { get; set; }
        public Guid PlanId { get; set; }
        public DateOnly ValidUntil { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1));
        public int MaxUsers { get; set; } = 1;
    }
}
