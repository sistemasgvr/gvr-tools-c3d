using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace GvrLicense.Infrastructure.Signing;

/// <summary>
/// Emite el JWT (ES256, misma clave ECDsa P-256 que firma el blob de entitlements) que el add-in
/// manda como <c>Authorization: Bearer</c> en /v1/heartbeat y /v1/usage. La validación en sí la
/// hace el middleware de ASP.NET Core (AddJwtBearer en Program.cs) contra la misma clave -- esta
/// clase solo firma, no valida.
/// </summary>
public sealed class JwtSessionTokenService : IDisposable
{
    public const string Issuer = "gvr-license-api";
    public const string Audience = "gvr-tools-addin";
    public const string LicenseIdClaim = "license_id";
    public const string DeviceIdClaim = "device_id";

    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(14);

    private readonly ECDsa _key;
    private readonly SigningCredentials _signingCredentials;

    public JwtSessionTokenService(IConfiguration configuration)
    {
        var pem = PemNormalizer.Normalize(configuration["Signing:PrivateKeyPem"]);
        if (string.IsNullOrWhiteSpace(pem))
        {
            throw new InvalidOperationException("Signing:PrivateKeyPem no está configurado.");
        }

        _key = ECDsa.Create();
        _key.ImportFromPem(pem);
        _signingCredentials = new SigningCredentials(new ECDsaSecurityKey(_key), SecurityAlgorithms.EcdsaSha256);
    }

    public string Issue(Guid licenseId, Guid deviceId)
    {
        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims:
            [
                new Claim(LicenseIdClaim, licenseId.ToString()),
                new Claim(DeviceIdClaim, deviceId.ToString())
            ],
            expires: DateTime.UtcNow.Add(Lifetime),
            signingCredentials: _signingCredentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public void Dispose() => _key.Dispose();
}
