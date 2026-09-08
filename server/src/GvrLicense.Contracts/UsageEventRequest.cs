namespace GvrLicense.Contracts;

/// <summary>
/// Id = EventId generado por el cliente; la idempotencia se resuelve por constraint única en
/// Postgres. El JWT no va aquí: viaja como "Authorization: Bearer" (ver ActivateResponse.AccessToken).
/// </summary>
public sealed class UsageEventRequest
{
    public required string DeviceFingerprint { get; init; }
    public required Guid EventId { get; init; }
    public required string FeatureCode { get; init; }
    public required int Quantity { get; init; }
    public required DateTimeOffset OccurredAtUtc { get; init; }
}

public sealed class UsageEventResponse
{
    /// <summary>-1 = ilimitado. Null si el evento no pudo procesarse (ver LicenseEngine.ReportUsageAsync).</summary>
    public int? Remaining { get; init; }
}
