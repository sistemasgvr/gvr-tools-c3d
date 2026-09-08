using System;
using System.Runtime.Serialization;

namespace GvrTools.Licensing.Http.Dto
{
    [DataContract]
    public sealed class UsageEventDto
    {
        [DataMember(Name = "deviceFingerprint")]
        public string DeviceFingerprint { get; set; }

        [DataMember(Name = "eventId")]
        public Guid EventId { get; set; }

        [DataMember(Name = "featureCode")]
        public string FeatureCode { get; set; }

        [DataMember(Name = "quantity")]
        public int Quantity { get; set; }

        // DateTimeOffset ignora DateTimeFormat y sale como {"DateTime":..,"OffsetMinutes":..}; el server no lo parsea.
        [DataMember(Name = "occurredAtUtc")]
        public DateTime OccurredAtUtc { get; set; }
    }

    [DataContract]
    public sealed class UsageEventResponse
    {
        [DataMember(Name = "remaining")]
        public int? Remaining { get; set; }
    }
}
