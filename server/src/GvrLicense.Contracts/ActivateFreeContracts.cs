namespace GvrLicense.Contracts;

/// <summary>
/// UI_FREEMIUM_PLAN.md §4.1: primer arranque del add-in sin license.dat válido. Nombre/correo son
/// opcionales -- a diferencia de /v1/activate, aquí no hay una key que ya identifique a un cliente
/// de pago, así que el registro puede ser completamente anónimo (ver LicenseEngine.ActivateFreeAsync).
/// </summary>
public sealed class ActivateFreeRequest
{
    public required string DeviceFingerprint { get; init; }
    public string? DeviceName { get; init; }
    public string? UserFullName { get; init; }
    public string? UserEmail { get; init; }
}
