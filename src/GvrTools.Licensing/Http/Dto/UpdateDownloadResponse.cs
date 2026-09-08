using System.Runtime.Serialization;

namespace GvrTools.Licensing.Http.Dto
{
    [DataContract]
    public sealed class UpdateDownloadResponse
    {
        [DataMember(Name = "location")]
        public string Location { get; set; }
    }
}
