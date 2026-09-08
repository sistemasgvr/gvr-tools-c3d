using System.Runtime.Serialization;

namespace GvrTools.Licensing.Http.Dto
{
    [DataContract]
    public sealed class UpdateCheckResponse
    {
        [DataMember(Name = "updateAvailable")]
        public bool UpdateAvailable { get; set; }

        [DataMember(Name = "latestVersion")]
        public string LatestVersion { get; set; }

        [DataMember(Name = "downloadUrl")]
        public string DownloadUrl { get; set; }

        [DataMember(Name = "releaseNotes")]
        public string ReleaseNotes { get; set; }
    }
}
