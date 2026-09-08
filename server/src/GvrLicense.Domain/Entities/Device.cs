namespace GvrLicense.Domain.Entities;

/// <summary>
/// Un dispositivo activado. El seat lo topa <c>License.MaxUsers</c> contando personas
/// (<see cref="CompanyUser"/>) distintas, no filas de Device: la misma persona puede activar en
/// más de un dispositivo (oficina + laptop) sin gastar un seat extra.
/// </summary>
public sealed class Device
{
    public Guid Id { get; set; }
    public Guid LicenseId { get; set; }
    public License? License { get; set; }

    public Guid CompanyUserId { get; set; }
    public CompanyUser? CompanyUser { get; set; }

    /// <summary>Hash de MachineGuid + volumen de sistema + SID -- nunca el dato crudo (ver GvrTools.Licensing/Device).</summary>
    public string Fingerprint { get; set; } = string.Empty;

    public string? DisplayName { get; set; }

    /// <summary>Primera activación / instalación registrada en este PC (no cambia en heartbeats).</summary>
    public DateTimeOffset ActivatedAtUtc { get; set; }

    /// <summary>Último contacto con el servidor (activate, activate-free reuso o heartbeat).</summary>
    public DateTimeOffset LastSeenUtc { get; set; }

    /// <summary>Última IP observada en activate / heartbeat (nullable: devices previos a esta columna).</summary>
    public string? LastIp { get; set; }

    /// <summary>
    /// Cuántas veces este device contactó al servidor (alta + reactivaciones + heartbeats).
    /// Sustituye spamear <c>audit_log</c> en cada heartbeat.
    /// </summary>
    public int SeenCount { get; set; }
}
