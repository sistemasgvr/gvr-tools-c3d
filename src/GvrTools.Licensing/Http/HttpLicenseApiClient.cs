using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GvrTools.Licensing.Http.Dto;

namespace GvrTools.Licensing.Http
{
    public sealed class HttpLicenseApiClient : ILicenseApiClient, IDisposable
    {
        private static readonly DataContractJsonSerializerSettings JsonSettings =
            new DataContractJsonSerializerSettings
            {
                DateTimeFormat = new DateTimeFormat("yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK")
            };

        private readonly HttpClient _http;
        private readonly bool _ownsHttp;
        private readonly string _baseUrl;

        public string BaseUrl => _baseUrl;

        public HttpLicenseApiClient(string baseUrl, HttpClient httpClient = null)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new ArgumentException("Base URL requerida.", nameof(baseUrl));

            _baseUrl = TrimSlash(baseUrl);

            if (httpClient != null)
            {
                _http = httpClient;
                _ownsHttp = false;
            }
            else
            {
                _http = new HttpClient
                {
                    BaseAddress = new Uri(_baseUrl + "/"),
                    Timeout = TimeSpan.FromSeconds(15)
                };
                _ownsHttp = true;
            }
        }

        public async Task<ActivateResponse> ActivateAsync(ActivateRequest request, CancellationToken ct)
        {
            return await PostJsonAsync<ActivateRequest, ActivateResponse>("v1/activate", request, accessToken: null, ct)
                .ConfigureAwait(false);
        }

        public async Task<ActivateResponse> ActivateFreeAsync(ActivateFreeRequest request, CancellationToken ct)
        {
            return await PostJsonAsync<ActivateFreeRequest, ActivateResponse>("v1/activate-free", request, accessToken: null, ct)
                .ConfigureAwait(false);
        }

        public async Task<HeartbeatResponse> HeartbeatAsync(string accessToken, HeartbeatRequest request, CancellationToken ct)
        {
            return await PostJsonAsync<HeartbeatRequest, HeartbeatResponse>("v1/heartbeat", request, accessToken, ct)
                .ConfigureAwait(false);
        }

        public async Task<UsageEventResponse> ReportUsageAsync(string accessToken, UsageEventDto usageEvent, CancellationToken ct)
        {
            return await PostJsonAsync<UsageEventDto, UsageEventResponse>("v1/usage", usageEvent, accessToken, ct)
                .ConfigureAwait(false);
        }

        public async Task<DeactivateResponse> DeactivateAsync(string accessToken, DeactivateRequest request, CancellationToken ct)
        {
            return await PostJsonAsync<DeactivateRequest, DeactivateResponse>("v1/deactivate", request, accessToken, ct)
                .ConfigureAwait(false);
        }

        public async Task<UpdateCheckResponse> CheckForUpdateAsync(string currentVersion, string revitVersion, CancellationToken ct)
        {
            var path = "v1/updates/check?version=" + Uri.EscapeDataString(currentVersion ?? string.Empty) +
                       "&revit=" + Uri.EscapeDataString(revitVersion ?? string.Empty);

            using (var response = await _http.GetAsync(path, ct).ConfigureAwait(false))
            {
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    throw CreateApiException(response.StatusCode, body);

                ThrowIfHtmlResponse(response, body);
                return Deserialize<UpdateCheckResponse>(body);
            }
        }

        public async Task<UpdateDownloadResponse> GetUpdateDownloadAsync(Guid releaseId, CancellationToken ct)
        {
            var path = "v1/updates/download/" + releaseId.ToString("D");
            using (var response = await _http.GetAsync(path, ct).ConfigureAwait(false))
            {
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    throw CreateApiException(response.StatusCode, body);

                ThrowIfHtmlResponse(response, body);
                return Deserialize<UpdateDownloadResponse>(body);
            }
        }

        public void Dispose()
        {
            if (_ownsHttp)
                _http.Dispose();
        }

        private async Task<TResponse> PostJsonAsync<TRequest, TResponse>(
            string relativePath,
            TRequest body,
            string accessToken,
            CancellationToken ct)
        {
            var json = Serialize(body);
            using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
            using (var request = new HttpRequestMessage(HttpMethod.Post, relativePath) { Content = content })
            {
                if (!string.IsNullOrEmpty(accessToken))
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                using (var response = await _http.SendAsync(request, ct).ConfigureAwait(false))
                {
                    var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                        throw CreateApiException(response.StatusCode, responseBody);

                    ThrowIfHtmlResponse(response, responseBody);
                    return Deserialize<TResponse>(responseBody);
                }
            }
        }

        private static LicenseApiClientException CreateApiException(System.Net.HttpStatusCode status, string body)
        {
            var detail = TryReadProblemDetail(body) ?? ("Error HTTP " + (int)status);
            return new LicenseApiClientException((int)status, detail);
        }

        private static void ThrowIfHtmlResponse(HttpResponseMessage response, string body)
        {
            var contentType = response.Content.Headers.ContentType?.ToString();
            if ((!string.IsNullOrEmpty(body) && body.TrimStart().StartsWith("<", StringComparison.Ordinal)) ||
                (!string.IsNullOrEmpty(contentType) &&
                 contentType.IndexOf("html", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                throw new LicenseApiClientException(
                    502,
                    "La API devolvió HTML; revisa license-config.json: baseUrl debe ser http://localhost:5299 en desarrollo.");
            }
        }

        private static string TryReadProblemDetail(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return null;
            try
            {
                var problem = Deserialize<ProblemDetailsDto>(body);
                if (!string.IsNullOrWhiteSpace(problem?.Detail)) return problem.Detail;
                if (!string.IsNullOrWhiteSpace(problem?.Title)) return problem.Title;
            }
            catch
            {
                // fall through
            }

            return body.Length > 400 ? body.Substring(0, 400) : body;
        }

        private static string Serialize<T>(T value)
        {
            using (var stream = new MemoryStream())
            {
                var serializer = new DataContractJsonSerializer(typeof(T), JsonSettings);
                serializer.WriteObject(stream, value);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        private static T Deserialize<T>(string json)
        {
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json ?? "{}")))
            {
                var serializer = new DataContractJsonSerializer(typeof(T), JsonSettings);
                return (T)serializer.ReadObject(stream);
            }
        }

        private static string TrimSlash(string url) => url.TrimEnd('/');

        [DataContract]
        private sealed class ProblemDetailsDto
        {
            [DataMember(Name = "title")]
            public string Title { get; set; }

            [DataMember(Name = "detail")]
            public string Detail { get; set; }
        }
    }
}
