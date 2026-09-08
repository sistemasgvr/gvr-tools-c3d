namespace GvrLicense.Api.Services;

/// <summary>Mapea 1:1 a un código de estado HTTP -- V1Endpoints la atrapa y la convierte en Results.Problem.</summary>
public sealed class LicenseApiException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
