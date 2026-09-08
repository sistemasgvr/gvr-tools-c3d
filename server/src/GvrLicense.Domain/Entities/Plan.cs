namespace GvrLicense.Domain.Entities;

/// <summary>
/// Catálogo de features/límites editable en admin (docs/LICENSING_PLAN.md, "Catálogo de features").
/// Features es un diccionario libre -- "tool.batch_export": "true", "quota.sheets_per_month": "500" --
/// para que una tool nueva se habilite agregando una fila en admin, sin recompilar el API.
/// </summary>
public sealed class Plan
{
    public Guid Id { get; set; }

    /// <summary>Estable: "free", "starter", "pro". El add-in solo ve feature codes, no este código.</summary>
    public string Code { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public Dictionary<string, string> Features { get; set; } = [];

    /// <summary>
    /// Desactivar en vez de borrar: un plan con licencias ya vendidas no se puede eliminar (rompería
    /// la FK de License.PlanId), así que "descontinuarlo" es sacarlo del selector de licencias
    /// nuevas sin tocar las que ya lo usan.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Kill switch de emergencia (DDoS / abuso): cuando es true, la API pública rechaza
    /// activate / activate-free / heartbeat / usage para licencias de este plan. No cambia
    /// <see cref="IsActive"/> — el plan sigue en el catálogo admin y se puede asignar a
    /// licencias nuevas; solo se corta el servicio en vivo hasta reactivar.
    /// </summary>
    public bool ServiceSuspended { get; set; }
}
