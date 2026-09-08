namespace GvrLicense.Domain.Entities;

/// <summary>
/// Administrador del panel (docs/LICENSING_PLAN.md, Pieza 5). Auth: usuario + contraseña, sesión
/// por cookie tokenizada -- sin 2FA en v1. Vive en Postgres (no en configuración) para soportar
/// varios admins sin redeploy: se agregan desde /Admin/Users/Create una vez que hay al menos uno.
/// </summary>
public sealed class AdminUser
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;

    /// <summary>Formato "iteraciones.saltBase64.hashBase64" -- ver GvrLicense.Domain.Security.PasswordHasher.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>Desactivar en vez de borrar: conserva la referencia en AuditLog.Actor.</summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; }
}
