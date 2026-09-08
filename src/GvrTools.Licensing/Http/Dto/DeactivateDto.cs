using System.Runtime.Serialization;

namespace GvrTools.Licensing.Http.Dto
{
    [DataContract]
    public sealed class DeactivateRequest
    {
        [DataMember(Name = "deviceFingerprint")]
        public string DeviceFingerprint { get; set; }
    }

    [DataContract]
    public sealed class DeactivateResponse
    {
        [DataMember(Name = "deactivated")]
        public bool Deactivated { get; set; }
    }
}
