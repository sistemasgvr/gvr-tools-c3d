namespace GvrLicense.Domain.Entities;

/// <summary>
/// Persona dentro de un Customer (docs/LICENSING_PLAN.md, "Métodos de suscripción"). El seat se
/// cuenta por persona, no por dispositivo: alguien puede activar en su PC de oficina y su laptop
/// sin gastar dos seats -- ver License.MaxUsers y LicenseEngine.ActivateAsync. Se crea sola la
/// primera vez que alguien activa con ese correo (no hay alta manual en v1, igual que Device).
/// </summary>
public sealed class CompanyUser
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    /// <summary>Desactivar en vez de borrar: no libera el seat automáticamente, es una decisión manual del admin.</summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public List<Device> Devices { get; set; } = [];
}
