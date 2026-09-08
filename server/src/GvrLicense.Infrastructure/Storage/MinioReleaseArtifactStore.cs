using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using Microsoft.Extensions.Options;

namespace GvrLicense.Infrastructure.Storage;

public sealed class MinioReleaseArtifactStore : IReleaseArtifactStore, IDisposable
{
    private const long MultipartThresholdBytes = 16 * 1024 * 1024; // 16 MB
    private const long MultipartPartSizeBytes = 16 * 1024 * 1024;

    private readonly MinioOptions _options;
    private readonly IAmazonS3? _s3;
    private readonly TransferUtility? _transfer;

    public MinioReleaseArtifactStore(IOptions<MinioOptions> options)
    {
        _options = options.Value;
        if (IsConfigured)
        {
            var config = new AmazonS3Config
            {
                ServiceURL = _options.Endpoint.TrimEnd('/'),
                ForcePathStyle = true,
                AuthenticationRegion = "us-east-1",
                // PutObject simple de ~400 MB suele cortarse (~100s por defecto) y el SDK
                // reintenta desde 0: eso se ve como "la carga se reinicia".
                Timeout = TimeSpan.FromHours(2),
                MaxErrorRetry = 1
            };
            _s3 = new AmazonS3Client(_options.AccessKey, _options.SecretKey, config);
            _transfer = new TransferUtility(_s3);
        }
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_options.Endpoint) &&
        !string.IsNullOrWhiteSpace(_options.AccessKey) &&
        !string.IsNullOrWhiteSpace(_options.SecretKey) &&
        !string.IsNullOrWhiteSpace(_options.Bucket);

    public async Task EnsureBucketAsync(CancellationToken ct)
    {
        EnsureClient();
        var buckets = await _s3!.ListBucketsAsync(ct);
        if (buckets.Buckets.Any(b => string.Equals(b.BucketName, _options.Bucket, StringComparison.Ordinal)))
        {
            return;
        }

        await _s3.PutBucketAsync(new PutBucketRequest { BucketName = _options.Bucket }, ct);
    }

    public async Task<string> UploadAsync(
        Stream content,
        string version,
        string fileName,
        string contentType,
        CancellationToken ct,
        IProgress<(long Transferred, long Total)>? progress = null)
    {
        EnsureClient();
        await EnsureBucketAsync(ct);

        var safeVersion = SanitizeSegment(version);
        var safeName = SanitizeFileName(fileName);
        var objectKey = $"releases/{safeVersion}/{safeName}";
        var contentTypeValue = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType;
        var totalHint = content.CanSeek ? content.Length : 0L;

        // Archivos grandes: multipart (partes de 16 MB). Evita timeout/reinicio del PutObject único.
        if (totalHint >= MultipartThresholdBytes)
        {
            var uploadRequest = new TransferUtilityUploadRequest
            {
                BucketName = _options.Bucket,
                Key = objectKey,
                InputStream = content,
                ContentType = contentTypeValue,
                AutoCloseStream = false,
                PartSize = MultipartPartSizeBytes,
                DisablePayloadSigning = true
            };

            if (progress is not null)
            {
                uploadRequest.UploadProgressEvent += (_, args) =>
                {
                    var total = args.TotalBytes > 0 ? args.TotalBytes : totalHint;
                    progress.Report((args.TransferredBytes, total));
                };
            }

            await _transfer!.UploadAsync(uploadRequest, ct);
        }
        else
        {
            var request = new PutObjectRequest
            {
                BucketName = _options.Bucket,
                Key = objectKey,
                InputStream = content,
                ContentType = contentTypeValue,
                AutoCloseStream = false,
                DisablePayloadSigning = true
            };

            if (progress is not null)
            {
                request.StreamTransferProgress += (_, args) =>
                {
                    var total = args.TotalBytes > 0 ? args.TotalBytes : totalHint;
                    progress.Report((args.TransferredBytes, total));
                };
            }

            await _s3!.PutObjectAsync(request, ct);
        }

        progress?.Report((totalHint > 0 ? totalHint : 1, totalHint > 0 ? totalHint : 1));
        return objectKey;
    }

    public Task<string> CreatePresignedGetUrlAsync(string objectKey, CancellationToken ct)
    {
        EnsureClient();
        ct.ThrowIfCancellationRequested();

        var expiry = TimeSpan.FromMinutes(Math.Clamp(_options.PresignExpiryMinutes, 5, 24 * 60));
        var url = _s3!.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _options.Bucket,
            Key = objectKey,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.Add(expiry)
        });

        return Task.FromResult(url);
    }

    public void Dispose()
    {
        _transfer?.Dispose();
        _s3?.Dispose();
    }

    private void EnsureClient()
    {
        if (_s3 is null || !IsConfigured)
        {
            throw new InvalidOperationException(
                "MinIO no está configurado. Define Minio__Endpoint, Minio__AccessKey, Minio__SecretKey y Minio__Bucket.");
        }
    }

    private static string SanitizeSegment(string value)
    {
        var trimmed = value.Trim().Replace('\\', '/');
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            trimmed = trimmed.Replace(c, '-');
        }

        return string.IsNullOrWhiteSpace(trimmed) ? "unknown" : trimmed;
    }

    private static string SanitizeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName.Replace('\\', '/'));
        return string.IsNullOrWhiteSpace(name) ? "artifact.bin" : SanitizeSegment(name);
    }
}
