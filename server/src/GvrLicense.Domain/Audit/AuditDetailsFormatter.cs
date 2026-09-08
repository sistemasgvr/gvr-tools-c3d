using System.Text.Json;

namespace GvrLicense.Domain.Audit;

/// <summary>
/// Resume <c>DetailsJson</c> de auditoría para la UI admin (IP, fingerprint, nombre de PC)
/// sin mostrar el JSON crudo completo.
/// </summary>
public static class AuditDetailsFormatter
{
    public static string Summarize(string? detailsJson)
    {
        if (string.IsNullOrWhiteSpace(detailsJson))
        {
            return "—";
        }

        try
        {
            using var doc = JsonDocument.Parse(detailsJson);
            var root = doc.RootElement;
            var parts = new List<string>(4);

            Append(parts, "IP", TryString(root, "ip"));
            Append(parts, "PC", TryString(root, "deviceName"));

            var fingerprint = TryString(root, "fingerprint");
            if (!string.IsNullOrWhiteSpace(fingerprint))
            {
                var shortFp = fingerprint.Length > 16 ? fingerprint[..16] + "…" : fingerprint;
                parts.Add($"FP {shortFp}");
            }

            var reason = TryString(root, "reason");
            if (!string.IsNullOrWhiteSpace(reason))
            {
                parts.Add(reason!);
            }

            if (parts.Count > 0)
            {
                return string.Join(" · ", parts);
            }

            // Status change u otros shapes: deja un preview corto del JSON.
            return detailsJson.Length > 80 ? detailsJson[..80] + "…" : detailsJson;
        }
        catch (JsonException)
        {
            return detailsJson.Length > 80 ? detailsJson[..80] + "…" : detailsJson;
        }
    }

    public static string? TryGetIp(string? detailsJson) => TryGetProperty(detailsJson, "ip");

    public static string? TryGetFingerprint(string? detailsJson) => TryGetProperty(detailsJson, "fingerprint");

    private static string? TryGetProperty(string? detailsJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(detailsJson))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(detailsJson);
            return TryString(doc.RootElement, propertyName);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? TryString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var value = element.GetString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static void Append(List<string> parts, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add($"{label} {value}");
        }
    }
}
