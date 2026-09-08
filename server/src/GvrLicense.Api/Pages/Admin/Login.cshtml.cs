using System.Security.Claims;
using GvrLicense.Domain.Security;
using GvrLicense.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GvrLicense.Api.Pages.Admin;

/// <summary>
/// Usuario + contraseña, sesión por cookie tokenizada -- sin 2FA en v1
/// (docs/LICENSING_PLAN.md, Pieza 5). Los admins viven en la tabla `admin_user`, no en
/// configuración: se agregan desde /Admin/Users/Create una vez que hay al menos uno
/// (server/tools/GenerateAdminBootstrap siembra el primero).
/// </summary>
[AllowAnonymous]
public class LoginModel(LicenseDbContext db) : PageModel
{
    [BindProperty]
    public LoginInput Input { get; set; } = new();

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await db.AdminUsers.FirstOrDefaultAsync(u => u.Username == Input.Username && u.IsActive);
        var ok = user != null && PasswordHasher.Verify(Input.Password, user.PasswordHash);

        if (!ok)
        {
            ModelState.AddModelError(string.Empty, "Usuario o contraseña incorrectos.");
            return Page();
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, user!.Username), new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())],
            CookieAuthenticationDefaults.AuthenticationScheme);

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

        return RedirectToPage("/Admin/Index");
    }

    public sealed class LoginInput
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }
}
