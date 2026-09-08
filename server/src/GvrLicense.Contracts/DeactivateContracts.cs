namespace GvrLicense.Contracts;

/// <summary>
/// POST /v1/deactivate -- libera el seat de este dispositivo (docs/LICENSING_PLAN.md, Pieza 2
/// "Desactivar este PC"). El JWT ya identifica license_id + device_id; el body solo confirma la
/// huella para no liberar otro PC si el token se filtró.
/// </summary>
public sealed class DeactivateRequest
{
    public required string DeviceFingerprint { get; init; }
}

public sealed class DeactivateResponse
{
    public bool Deactivated { get; init; }
}
