using System.Text.Json;
using GvrLicense.Domain.Entities;

namespace GvrLicense.Domain.Audit;

public static class AuditActionDescriber
{
    public static string Describe(string action, string? detailsJson = null)
    {
        if (string.Equals(action, "license_status_changed", StringComparison.Ordinal))
        {
            var (from, to) = TryParseStatusChange(detailsJson);
            if (to is not null)
            {
                return to switch
                {
                    "Active" => from == "Suspended" ? "Licencia reactivada" : "Licencia activada",
                    "Suspended" => "Licencia suspendida",
                    "Expired" => "Licencia vencida",
                    _ => $"Estado → {StatusLabel(to)}"
                };
            }

            return "Cambio de estado de licencia";
        }

        return action switch
        {
            "license.create" => "Licencia creada",
            "license.suspend" => "Licencia suspendida",
            "license.activate" => "Licencia activada con clave",
            "license.expire" => "Licencia vencida",
            "device.kick" => "PC liberado",
            "device.deactivate" => "Desactivación en cliente",
            "license.activate_free" => "Alta plan Free",
            "security.activate_free_denied" => "Registro Free rechazado",
            "plan.service_suspend" => "Servicio del plan suspendido",
            "plan.service_resume" => "Servicio del plan reactivado",
            _ => action.Replace(".", " ", StringComparison.Ordinal)
        };
    }

    private static (string? From, string? To) TryParseStatusChange(string? detailsJson)
    {
        if (string.IsNullOrWhiteSpace(detailsJson))
        {
            return (null, null);
        }

        try
        {
            using var doc = JsonDocument.Parse(detailsJson);
            var root = doc.RootElement;
            var from = ParseStatus(root, "from");
            var to = ParseStatus(root, "to");
            return (from, to);
        }
        catch (Exception)
        {
            return (null, null);
        }
    }

    // El trigger de Postgres escribe old.status/new.status como número, no como string.
    private static string? ParseStatus(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element))
        {
            return null;
        }

        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt32(out var raw) && Enum.IsDefined(typeof(LicenseStatus), raw)
                => ((LicenseStatus)raw).ToString(),
            _ => null
        };
    }

    private static string StatusLabel(string status) => status switch
    {
        "Active" => "activa",
        "Suspended" => "suspendida",
        "Expired" => "vencida",
        _ => status.ToLowerInvariant()
    };
}
