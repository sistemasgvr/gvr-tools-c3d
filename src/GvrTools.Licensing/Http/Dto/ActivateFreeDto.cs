using System.Runtime.Serialization;

namespace GvrTools.Licensing.Http.Dto
{
    /// <summary>UI_FREEMIUM_PLAN.md §4.1: primer arranque sin license.dat válido, sin key de pago.</summary>
    [DataContract]
    public sealed class ActivateFreeRequest
    {
        [DataMember(Name = "deviceFingerprint")]
        public string DeviceFingerprint { get; set; }

        [DataMember(Name = "deviceName")]
        public string DeviceName { get; set; }

        [DataMember(Name = "userFullName")]
        public string UserFullName { get; set; }

        [DataMember(Name = "userEmail")]
        public string UserEmail { get; set; }
    }
}
