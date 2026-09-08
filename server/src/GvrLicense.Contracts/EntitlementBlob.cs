namespace GvrLicense.Contracts;

/// <summary>
/// Forma exacta que se serializa a JSON y se firma con ECDsa P-256 (docs/LICENSING_PLAN.md, "Tokens
/// y gracia offline"). Espejo: src/GvrTools.Licensing/Entitlements/EntitlementBlob.cs.
///
/// Deliberadamente solo tipos primitivos (string, List de POCOs simples) -- nada de Dictionary ni
/// DateTimeOffset nativo. El cliente net48 no puede usar System.Text.Json (no viene con el
/// framework; agregarlo por NuGet rompería la regla de cero dependencias de runtime del add-in, ver
/// docs/ARCHITECTURE.md) y usa en su lugar DataContractJsonSerializer, que serializa Dictionary y
/// DateTimeOffset en formatos no estándar incompatibles con lo que produce System.Text.Json aquí.
/// Con solo strings y listas de POCOs, ambos serializadores producen y leen el mismo JSON.
/// </summary>
public sealed class EntitlementBlob
{
    public required string LicenseId { get; init; }
    public required string PlanCode { get; init; }
    public required List<FeatureEntry> Features { get; init; }

    /// <summary>ISO 8601 ("O") en UTC -- ver nota de la clase sobre por qué no DateTimeOffset nativo.</summary>
    public required string IssuedAtUtc { get; init; }

    /// <summary>ISO 8601 ("O") en UTC.</summary>
    public required string OfflineUntilUtc { get; init; }

    public required string DeviceId { get; init; }
}

public sealed class FeatureEntry
{
    public required string Code { get; init; }
    public required string Value { get; init; }
}
