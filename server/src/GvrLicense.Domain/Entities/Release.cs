namespace GvrLicense.Domain.Entities;

/// <summary>Un release publicado (docs/LICENSING_PLAN.md, Pieza 4). Canal único "stable" en v1.</summary>
public sealed class Release
{
    public Guid Id { get; set; }
    public string Version { get; set; } = string.Empty;
    public string Channel { get; set; } = "stable";
    public string Checksum { get; set; } = string.Empty;

    /// <summary>
    /// Object key en MinIO (ej. releases/1.0.0/GvrTools-Setup.exe), no la URL firmada.
    /// </summary>
    public string ArtifactLocation { get; set; } = string.Empty;

    /// <summary>
    /// <c>installer</c> = Setup .exe para descarga inicial del cliente (/download).
    /// <c>update</c> = paquete que ofrece el add-in vía /v1/updates/check.
    /// </summary>
    public string Kind { get; set; } = ReleaseKinds.Installer;

    public string? FileName { get; set; }
    public string? Notes { get; set; }
    public string SignatureBase64 { get; set; } = string.Empty;
    public DateTimeOffset PublishedAtUtc { get; set; }
}

public static class ReleaseKinds
{
    public const string Installer = "installer";
    public const string Update = "update";
}
