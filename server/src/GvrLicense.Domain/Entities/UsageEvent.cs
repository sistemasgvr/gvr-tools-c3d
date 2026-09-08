namespace GvrLicense.Domain.Entities;

/// <summary>
/// Registro crudo de cada POST /v1/usage, con Id = EventId generado por el cliente. La idempotencia
/// (regla 6 de "Reglas de consumo") se resuelve con una constraint única sobre Id en
/// GvrLicense.Infrastructure, no en código C#.
/// </summary>
public sealed class UsageEvent
{
    public Guid Id { get; set; }
    public Guid LicenseId { get; set; }
    public Guid DeviceId { get; set; }
    public string FeatureCode { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public DateTimeOffset ReceivedAtUtc { get; set; }
}
