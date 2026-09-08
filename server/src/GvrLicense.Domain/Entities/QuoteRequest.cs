namespace GvrLicense.Domain.Entities;

public enum QuoteRequestStatus
{
    New,
    Contacted,
    Closed
}

/// <summary>
/// Formulario público "Cotiza" en la landing de descarga: alguien interesado deja sus datos, el
/// admin le da seguimiento manual (sin CRM externo, igual que el resto del sistema -- ver
/// RUNBOOK_LICENSING.md, "cobro y entrega manual"). PlanCode es una copia del código al momento de
/// enviar el formulario, no una FK: el plan puede renombrarse o descontinuarse después sin que la
/// cotización pierda sentido.
/// </summary>
public sealed class QuoteRequest
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? CompanyName { get; set; }
    public string? PlanCode { get; set; }
    public string? Message { get; set; }
    public QuoteRequestStatus Status { get; set; } = QuoteRequestStatus.New;
    public string? SourceIp { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
