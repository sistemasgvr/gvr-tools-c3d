namespace GvrLicense.Domain.Entities;

public enum LicenseStatus
{
    Active,
    Suspended,
    Expired
}

public sealed class License
{
    public Guid Id { get; set; }

    /// <summary>Formato GVR-XXXX-XXXX-XXXX (Base32 Crockford aleatorio + checksum).</summary>
    public string Key { get; set; } = string.Empty;

    public Guid CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public Guid PlanId { get; set; }
    public Plan? Plan { get; set; }

    public LicenseStatus Status { get; set; }
    public DateTimeOffset ValidUntil { get; set; }

    /// <summary>Tope de personas (CompanyUser) distintas, no de dispositivos. Ver Entities/Device.cs.</summary>
    public int MaxUsers { get; set; }

    /// <summary>
    /// Features propias encima del Plan base (docs/LICENSING_PLAN.md, "Métodos de suscripción" --
    /// plan "de por vida... con venta de funcionalidades extra"). Al armar el blob de entitlements
    /// se mezcla con Plan.Features; si una key se repite en ambos, gana este override. No hace
    /// falta crear un Plan nuevo por cada extra que le vendas a un cliente puntual.
    /// </summary>
    public Dictionary<string, string> FeatureOverrides { get; set; } = [];

    public DateTimeOffset CreatedAtUtc { get; set; }

    public List<Device> Devices { get; set; } = [];
    public List<UsageCounter> UsageCounters { get; set; } = [];
}
