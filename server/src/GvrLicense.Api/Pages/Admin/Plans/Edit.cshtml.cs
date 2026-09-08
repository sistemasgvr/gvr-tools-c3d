using GvrLicense.Domain.Entities;
using GvrLicense.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GvrLicense.Api.Pages.Admin.Plans;

public class EditModel(LicenseDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    [BindProperty]
    public PlanInput Input { get; set; } = new();

    public string Code { get; private set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync()
    {
        var plan = await db.Plans.FindAsync(Id);
        if (plan is null)
        {
            return NotFound();
        }

        Code = plan.Code;
        Input = new PlanInput
        {
            DisplayName = plan.DisplayName,
            ServiceSuspended = plan.ServiceSuspended,
            Features = PlanFeatureForm.FromDictionary(plan.Features)
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var plan = await db.Plans.FindAsync(Id);
        if (plan is null)
        {
            return NotFound();
        }

        var wasSuspended = plan.ServiceSuspended;
        plan.DisplayName = Input.DisplayName.Trim();
        plan.ServiceSuspended = Input.ServiceSuspended;
        plan.Features = Input.Features.ToDictionary();

        if (wasSuspended != plan.ServiceSuspended)
        {
            db.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                LicenseId = null,
                Actor = User.Identity?.Name ?? "admin",
                Action = plan.ServiceSuspended ? "plan.service_suspend" : "plan.service_resume",
                DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    planId = plan.Id,
                    code = plan.Code,
                    displayName = plan.DisplayName,
                    serviceSuspended = plan.ServiceSuspended
                }),
                OccurredAtUtc = DateTimeOffset.UtcNow
            });
        }

        await db.SaveChangesAsync();

        Code = plan.Code;
        TempData["Saved"] = true;
        return RedirectToPage(new { id = Id });
    }

    public sealed class PlanInput
    {
        public string DisplayName { get; set; } = string.Empty;
        public bool ServiceSuspended { get; set; }
        public PlanFeatureForm Features { get; set; } = new();
    }
}
