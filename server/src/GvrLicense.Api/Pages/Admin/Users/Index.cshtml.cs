using System.Security.Claims;
using GvrLicense.Domain.Security;
using GvrLicense.Infrastructure;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GvrLicense.Api.Pages.Admin.Users;

public class IndexModel(LicenseDbContext db, IAntiforgery antiforgery) : PageModel
{
    public List<AdminRow> Rows { get; private set; } = [];

    /// <summary>Tabulator genera el botón Activar/Desactivar por fila como HTML crudo, necesita el token a mano.</summary>
    public string AntiForgeryToken { get; private set; } = string.Empty;

    public async Task OnGetAsync()
    {
        AntiForgeryToken = antiforgery.GetAndStoreTokens(HttpContext).RequestToken!;

        var currentUsername = User.Identity?.Name;
        Rows = await db.AdminUsers
            .OrderBy(u => u.Username)
            .Select(u => new AdminRow(u.Id, u.Username, u.IsActive, u.CreatedAtUtc, u.Username == currentUsername))
            .ToListAsync();
    }

    /// <summary>Nunca te desactivas a ti mismo -- te dejaría sin poder volver a entrar sin la herramienta de sembrado.</summary>
    public async Task<IActionResult> OnPostToggleAsync(Guid userId)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId.ToString() == currentUserId)
        {
            TempData["Error"] = "No puedes desactivar tu propia cuenta.";
            return RedirectToPage();
        }

        var user = await db.AdminUsers.FindAsync(userId);
        if (user != null)
        {
            user.IsActive = !user.IsActive;
            await db.SaveChangesAsync();
        }

        return RedirectToPage();
    }

    /// <summary>Cualquier admin puede resetear la contraseña de otro (incluida la propia) -- no hay flujo de "olvidé mi contraseña" por correo, ver LICENSING_PLAN.md Pieza 5.</summary>
    public async Task<IActionResult> OnPostResetPasswordAsync(Guid userId, string newPassword)
    {
        var user = await db.AdminUsers.FindAsync(userId);
        if (user != null && !string.IsNullOrWhiteSpace(newPassword))
        {
            user.PasswordHash = PasswordHasher.Hash(newPassword);
            await db.SaveChangesAsync();
        }

        return RedirectToPage();
    }

    public sealed record AdminRow(Guid Id, string Username, bool IsActive, DateTimeOffset CreatedAtUtc, bool IsSelf);
}
