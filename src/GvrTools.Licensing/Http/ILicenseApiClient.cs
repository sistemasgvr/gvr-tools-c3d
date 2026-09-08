using System;
using System.Threading;
using System.Threading.Tasks;
using GvrTools.Licensing.Http.Dto;

namespace GvrTools.Licensing.Http
{
    /// <summary>
    /// Envoltorio sobre HttpClient para los endpoints de docs/LICENSING_PLAN.md ("API mínima v1")
    /// más /v1/deactivate. Sin NuGet: HttpClient + DataContractJsonSerializer.
    /// </summary>
    public interface ILicenseApiClient
    {
        string BaseUrl { get; }

        Task<ActivateResponse> ActivateAsync(ActivateRequest request, CancellationToken ct);

        Task<ActivateResponse> ActivateFreeAsync(ActivateFreeRequest request, CancellationToken ct);

        Task<HeartbeatResponse> HeartbeatAsync(string accessToken, HeartbeatRequest request, CancellationToken ct);

        Task<UsageEventResponse> ReportUsageAsync(string accessToken, UsageEventDto usageEvent, CancellationToken ct);

        Task<DeactivateResponse> DeactivateAsync(string accessToken, DeactivateRequest request, CancellationToken ct);

        Task<UpdateCheckResponse> CheckForUpdateAsync(string currentVersion, string revitVersion, CancellationToken ct);

        /// <summary>GET /v1/updates/download/{id} → Location (URL firmada MinIO).</summary>
        Task<UpdateDownloadResponse> GetUpdateDownloadAsync(Guid releaseId, CancellationToken ct);
    }

    /// <summary>Error HTTP del License API (ProblemDetails u otro cuerpo).</summary>
    public sealed class LicenseApiClientException : Exception
    {
        public LicenseApiClientException(int statusCode, string message)
            : base(message)
        {
            StatusCode = statusCode;
        }

        public int StatusCode { get; }
    }
}
