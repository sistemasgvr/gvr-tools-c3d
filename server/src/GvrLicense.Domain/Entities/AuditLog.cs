namespace GvrLicense.Domain.Entities;

/// <summary>
/// Quién activó/renovó/suspendió qué. Los cambios de License.Status se insertan solos vía trigger
/// AFTER UPDATE (ver GvrLicense.Infrastructure/Sql/AuditLogTrigger.sql) para que el rastro exista
/// aunque alguien edite la tabla License directo en psql; el resto de acciones (crear cliente,
/// renovar valid_until, kick de un device) las inserta el admin explícitamente.
/// </summary>
public sealed class AuditLog
{
    public Guid Id { get; set; }
    public Guid? LicenseId { get; set; }
    public string Actor { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? DetailsJson { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
}
