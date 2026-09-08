namespace GvrLicense.Domain.Entities;

public sealed class Customer
{
    public Guid Id { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string ContactName { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;

    /// <summary>Notas de pago (precio acordado, forma de pago). Nunca vive en el add-in.</summary>
    public string? PaymentNotes { get; set; }

    /// <summary>
    /// Desactivar en vez de borrar (mismo criterio que AdminUser/CompanyUser): es solo un flag
    /// administrativo para dejar de mostrar clientes que ya no son tuyos, no bloquea nada del motor
    /// de licencias por sí solo -- para eso se suspende la License puntual.
    /// </summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public List<License> Licenses { get; set; } = [];
}
