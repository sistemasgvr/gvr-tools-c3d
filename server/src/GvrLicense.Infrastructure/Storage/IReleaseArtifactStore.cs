namespace GvrLicense.Infrastructure.Storage;

/// <summary>Subida y URLs firmadas de artefactos de release (bucket privado MinIO).</summary>
public interface IReleaseArtifactStore
{
    bool IsConfigured { get; }

    /// <summary>
    /// Sube el archivo al bucket. Devuelve la object key (ej. releases/1.0.0/setup.exe)
    /// que se guarda en <c>Release.ArtifactLocation</c>.
    /// </summary>
    /// <param name="progress">
    /// Bytes enviados a MinIO (Transferred/Total). Null si no hace falta reportar.
    /// </param>
    Task<string> UploadAsync(
        Stream content,
        string version,
        string fileName,
        string contentType,
        CancellationToken ct,
        IProgress<(long Transferred, long Total)>? progress = null);

    /// <summary>URL firmada temporal (GET) para un object key del bucket.</summary>
    Task<string> CreatePresignedGetUrlAsync(string objectKey, CancellationToken ct);

    /// <summary>Comprueba que el bucket exista (lo crea si falta y hay permisos).</summary>
    Task EnsureBucketAsync(CancellationToken ct);
}
