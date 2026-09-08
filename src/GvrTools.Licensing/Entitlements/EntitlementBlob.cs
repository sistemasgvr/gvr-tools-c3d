using System.Runtime.Serialization;

namespace GvrTools.Licensing.Entitlements
{
    /// <summary>
    /// Forma parseada del blob firmado que devuelve el License API en /v1/activate y /v1/heartbeat.
    /// Espejo de EntitlementBlob en GvrLicense.Contracts (server/); no se comparte código porque el
    /// cliente no puede referenciar un proyecto net10.0 desde net48 -- debe mantenerse sincronizado
    /// campo a campo a mano (ver docs/LICENSING_PLAN.md, "Tokens y gracia offline").
    ///
    /// Solo tipos primitivos a propósito (string, List, nada de Dictionary/DateTimeOffset nativo):
    /// este tipo se deserializa con DataContractJsonSerializer (net48 no tiene System.Text.Json en
    /// el framework), que no es compatible byte a byte con la salida de System.Text.Json del
    /// servidor para Dictionary ni DateTimeOffset.
    /// </summary>
    [DataContract]
    public sealed class EntitlementBlob
    {
        [DataMember]
        public string LicenseId { get; set; }

        [DataMember]
        public string PlanCode { get; set; }

        [DataMember]
        public System.Collections.Generic.List<FeatureEntry> Features { get; set; }

        [DataMember]
        public string IssuedAtUtc { get; set; }

        [DataMember]
        public string OfflineUntilUtc { get; set; }

        [DataMember]
        public string DeviceId { get; set; }
    }

    [DataContract]
    public sealed class FeatureEntry
    {
        [DataMember]
        public string Code { get; set; }

        [DataMember]
        public string Value { get; set; }
    }
}
