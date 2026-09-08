using System.Runtime.Serialization;

namespace GvrTools.Licensing.Http.Dto
{
    [DataContract]
    public sealed class HeartbeatRequest
    {
        [DataMember(Name = "deviceFingerprint")]
        public string DeviceFingerprint { get; set; }
    }

    [DataContract]
    public sealed class HeartbeatResponse
    {
        /// <summary>JWT renovado — reemplazar el AccessToken local.</summary>
        [DataMember(Name = "accessToken")]
        public string AccessToken { get; set; }

        [DataMember(Name = "entitlementJson")]
        public string EntitlementJson { get; set; }

        [DataMember(Name = "entitlementSignatureBase64")]
        public string EntitlementSignatureBase64 { get; set; }
    }
}
