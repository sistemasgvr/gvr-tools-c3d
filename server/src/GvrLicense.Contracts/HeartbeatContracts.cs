namespace GvrLicense.Contracts;

/// <summary>El JWT de request viaja como "Authorization: Bearer". La respuesta renueva el AccessToken.</summary>
public sealed class HeartbeatRequest
{
    public required string DeviceFingerprint { get; init; }
}

public sealed class HeartbeatResponse
{
    /// <summary>JWT renovado (14 días). El cliente debe reemplazar el token anterior.</summary>
    public required string AccessToken { get; init; }

    public required string EntitlementJson { get; init; }
    public required string EntitlementSignatureBase64 { get; init; }
}
