namespace GvrLicense.Domain.Entities;

/// <summary>Fila única editable en admin: soporte y textos legales (docs/LICENSING_PLAN.md, "Decisiones fijadas").</summary>
public sealed class AppSettings
{
    public Guid Id { get; set; }
    public string SupportEmail { get; set; } = string.Empty;
    public string TermsOfServiceUrl { get; set; } = string.Empty;
    public string PrivacyPolicyUrl { get; set; } = string.Empty;
}
