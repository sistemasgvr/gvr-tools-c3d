namespace GvrLicense.Infrastructure.Storage;

/// <summary>
/// Config MinIO / S3-compatible. En EasyPanel: Minio__Endpoint, Minio__AccessKey,
/// Minio__SecretKey, Minio__Bucket (nunca en git).
/// </summary>
public sealed class MinioOptions
{
    public const string SectionName = "Minio";

    /// <summary>API del servidor, ej. https://sistemas-gvr-minio.odjkys.easypanel.host (no la consola).</summary>
    public string Endpoint { get; set; } = string.Empty;

    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string Bucket { get; set; } = "gvr-tools-releases";

    /// <summary>Vida de las URLs firmadas de descarga (minutos).</summary>
    public int PresignExpiryMinutes { get; set; } = 60;
}
