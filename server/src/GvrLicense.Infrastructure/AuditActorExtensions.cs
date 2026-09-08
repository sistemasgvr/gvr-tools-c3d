using Microsoft.EntityFrameworkCore;

namespace GvrLicense.Infrastructure;

public static class AuditActorExtensions
{
    /// <summary>
    /// El trigger audit_license_status_change lee gvr.actor para saber quién cambió el estado.
    /// set_config(..., true) aplica solo a la transacción actual.
    /// </summary>
    public static Task SetAuditActorAsync(this LicenseDbContext db, string actor, CancellationToken ct = default) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('gvr.actor', {actor}, true)", ct);
}
