using GvrLicense.Domain.Entities;
using GvrLicense.Domain.Security;
using GvrLicense.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GvrLicense.Api.Pages.Admin.Users;

/// <summary>
/// Solo accesible ya logueado (misma carpeta /Admin, ver Program.cs AuthorizeFolder). Reemplaza a
/// server/tools/GenerateAdminBootstrap para altas del día a día -- esa herramienta sigue existiendo
/// solo para sembrar el primer admin cuando la tabla está vacía.
/// </summary>
public class CreateModel(LicenseDbContext db) : PageModel
{
    [BindProperty]
    public UserInput Input { get; set; } = new();

    public bool Created { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var exists = await db.AdminUsers.AnyAsync(u => u.Username == Input.Username);
        if (exists)
        {
            ModelState.AddModelError(string.Empty, "Ya existe un administrador con ese usuario.");
            return Page();
        }

        db.AdminUsers.Add(new AdminUser
        {
            Id = Guid.NewGuid(),
            Username = Input.Username,
            PasswordHash = PasswordHasher.Hash(Input.Password),
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        Created = true;
        return Page();
    }

    public sealed class UserInput
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }
}
