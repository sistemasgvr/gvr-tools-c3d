namespace GvrLicense.Domain.Entities;

/// <summary>
/// Contador por mes calendario UTC (docs/LICENSING_PLAN.md, "Reglas de consumo" regla 5). Un mes
/// nuevo es una fila nueva -- no hace falta job de reset, ver "Dónde vive la lógica: app vs Postgres".
/// El incremento atómico corre en la función SQL consume_quota (server/src/GvrLicense.Infrastructure/Sql),
/// no aquí: esta clase es solo la forma de la fila para EF Core.
/// </summary>
public sealed class UsageCounter
{
    public Guid Id { get; set; }
    public Guid LicenseId { get; set; }
    public License? License { get; set; }

    public string FeatureCode { get; set; } = string.Empty;

    /// <summary>Primer día del mes UTC que este contador cubre.</summary>
    public DateOnly Period { get; set; }

    /// <summary>-1 = ilimitado (mismo significado que quota.sheets_per_month en el catálogo de features).</summary>
    public int QuotaLimit { get; set; }

    public int Consumed { get; set; }
}
