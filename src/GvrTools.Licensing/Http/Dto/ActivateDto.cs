using System;
using System.Runtime.Serialization;

namespace GvrTools.Licensing.Http.Dto
{
    [DataContract]
    public sealed class ActivateRequest
    {
        [DataMember(Name = "licenseKey")]
        public string LicenseKey { get; set; }

        [DataMember(Name = "deviceFingerprint")]
        public string DeviceFingerprint { get; set; }

        [DataMember(Name = "deviceName")]
        public string DeviceName { get; set; }

        [DataMember(Name = "userFullName")]
        public string UserFullName { get; set; }

        [DataMember(Name = "userEmail")]
        public string UserEmail { get; set; }
    }

    [DataContract]
    public sealed class ActivateResponse
    {
        [DataMember(Name = "accessToken")]
        public string AccessToken { get; set; }

        [DataMember(Name = "entitlementJson")]
        public string EntitlementJson { get; set; }

        [DataMember(Name = "entitlementSignatureBase64")]
        public string EntitlementSignatureBase64 { get; set; }
    }
}
