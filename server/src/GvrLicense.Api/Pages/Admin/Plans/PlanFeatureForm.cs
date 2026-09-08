using System.ComponentModel.DataAnnotations;

namespace GvrLicense.Api.Pages.Admin.Plans;

/// <summary>
/// Formulario amigable del catálogo v1 (docs/LICENSING_PLAN.md).
/// Los códigos técnicos se guardan en el diccionario del Plan; la UI no los muestra al dueño.
/// </summary>
public sealed class PlanFeatureForm
{
    [Display(Name = "Exportación masiva de láminas")]
    public bool ToolBatchExport { get; set; } = true;

    [Display(Name = "PDF")]
    public bool FormatPdf { get; set; } = true;

    [Display(Name = "DWG")]
    public bool FormatDwg { get; set; }

    [Display(Name = "PDF + DWG en una pasada")]
    public bool FormatPdfDwg { get; set; }

    [Display(Name = "Puede recibir actualizaciones del add-in")]
    public bool UpdatesStable { get; set; } = true;

    /// <summary>Láminas/mes. Se ignora si <see cref="SheetsPerMonthUnlimited"/>.</summary>
    [Display(Name = "Láminas por mes")]
    public int SheetsPerMonth { get; set; } = 500;

    [Display(Name = "Ilimitadas")]
    public bool SheetsPerMonthUnlimited { get; set; }

    [Display(Name = "Máx. láminas por lote")]
    public int SheetsPerBatch { get; set; } = 100;

    /// <summary>PCs por usuario de la licencia (feature <c>seat.max_devices_per_user</c>).</summary>
    [Display(Name = "PCs por usuario")]
    public int MaxDevicesPerUser { get; set; } = 1;

    /// <summary>Códigos fuera del catálogo v1 (avanzado). Una línea <c>codigo=valor</c>.</summary>
    [Display(Name = "Extras (una por línea, codigo=valor)")]
    public string? ExtraFeaturesText { get; set; }

    public static readonly HashSet<string> KnownCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "tool.batch_export",
        "format.pdf",
        "format.dwg",
        "format.pdf_dwg",
        "updates.stable",
        "quota.sheets_per_month",
        "limit.sheets_per_batch",
        "seat.max_devices_per_user",
        "seat.max_devices", // alias legado al leer
    };

    public static PlanFeatureForm FromDictionary(IReadOnlyDictionary<string, string> features)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in features)
        {
            map[kv.Key] = kv.Value;
        }

        var form = new PlanFeatureForm
        {
            ToolBatchExport = IsTrue(map, "tool.batch_export", defaultValue: true),
            FormatPdf = IsTrue(map, "format.pdf", defaultValue: true),
            FormatDwg = IsTrue(map, "format.dwg"),
            FormatPdfDwg = IsTrue(map, "format.pdf_dwg"),
            UpdatesStable = IsTrue(map, "updates.stable", defaultValue: true),
            SheetsPerBatch = ReadInt(map, "limit.sheets_per_batch", 100),
            MaxDevicesPerUser = ReadInt(map, "seat.max_devices_per_user",
                fallbackKey: "seat.max_devices", defaultValue: 1),
        };

        var quota = ReadInt(map, "quota.sheets_per_month", 500);
        if (quota < 0)
        {
            form.SheetsPerMonthUnlimited = true;
            form.SheetsPerMonth = 500;
        }
        else
        {
            form.SheetsPerMonth = quota;
        }

        var extras = map
            .Where(kv => !KnownCodes.Contains(kv.Key))
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => $"{kv.Key}={kv.Value}");
        form.ExtraFeaturesText = string.Join('\n', extras);

        return form;
    }

    public Dictionary<string, string> ToDictionary()
    {
        var features = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["tool.batch_export"] = Bool(ToolBatchExport),
            ["format.pdf"] = Bool(FormatPdf),
            ["format.dwg"] = Bool(FormatDwg),
            ["format.pdf_dwg"] = Bool(FormatPdfDwg),
            ["updates.stable"] = Bool(UpdatesStable),
            ["quota.sheets_per_month"] = SheetsPerMonthUnlimited ? "-1" : Math.Max(0, SheetsPerMonth).ToString(),
            ["limit.sheets_per_batch"] = Math.Max(1, SheetsPerBatch).ToString(),
            ["seat.max_devices_per_user"] = Math.Max(1, MaxDevicesPerUser).ToString(),
        };

        foreach (var kv in IndexModel.ParseFeatures(ExtraFeaturesText))
        {
            if (KnownCodes.Contains(kv.Key) && !string.Equals(kv.Key, "seat.max_devices", StringComparison.OrdinalIgnoreCase))
            {
                continue; // el formulario ya manda estos
            }

            features[kv.Key.Trim().ToLowerInvariant()] = kv.Value;
        }

        return features;
    }

    public static string Summarize(IReadOnlyDictionary<string, string> features)
    {
        var form = FromDictionary(features);
        var parts = new List<string>();

        if (form.ToolBatchExport)
        {
            parts.Add("Exportación masiva");
        }

        var formats = new List<string>();
        if (form.FormatPdf) formats.Add("PDF");
        if (form.FormatDwg) formats.Add("DWG");
        if (form.FormatPdfDwg) formats.Add("PDF+DWG");
        if (formats.Count > 0)
        {
            parts.Add(string.Join(", ", formats));
        }

        parts.Add(form.SheetsPerMonthUnlimited
            ? "Láminas ilimitadas/mes"
            : $"{form.SheetsPerMonth} láminas/mes");
        parts.Add($"Hasta {form.SheetsPerBatch} por lote");
        parts.Add($"{form.MaxDevicesPerUser} PC(s) por usuario");
        if (form.UpdatesStable)
        {
            parts.Add("Actualizaciones");
        }

        return string.Join(" · ", parts);
    }

    /// <summary>Plan + overrides de licencia (gana el override si el código se repite).</summary>
    public static Dictionary<string, string> Merge(
        IReadOnlyDictionary<string, string> plan,
        IReadOnlyDictionary<string, string> overrides)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in plan)
        {
            merged[kv.Key] = kv.Value;
        }

        foreach (var kv in overrides)
        {
            merged[kv.Key] = kv.Value;
        }

        return merged;
    }

    /// <summary>
    /// Solo guarda diferencias respecto al plan. Así el formulario muestra lo efectivo
    /// (plan+overrides) y al guardar no duplica lo que ya viene del plan.
    /// </summary>
    public static Dictionary<string, string> DiffAgainstPlan(
        IReadOnlyDictionary<string, string> plan,
        IReadOnlyDictionary<string, string> desiredEffective)
    {
        var planNorm = FromDictionary(plan).ToDictionary();
        var desiredNorm = FromDictionary(desiredEffective).ToDictionary();
        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var kv in desiredNorm)
        {
            if (!planNorm.TryGetValue(kv.Key, out var planVal)
                || !string.Equals(planVal, kv.Value, StringComparison.Ordinal))
            {
                overrides[kv.Key] = kv.Value;
            }
        }

        return overrides;
    }

    private static string Bool(bool value) => value ? "true" : "false";

    private static bool IsTrue(IReadOnlyDictionary<string, string> features, string key, bool defaultValue = false)
    {
        if (!features.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return defaultValue;
        }

        return raw.Equals("true", StringComparison.OrdinalIgnoreCase)
            || raw == "1"
            || raw.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    private static int ReadInt(
        IReadOnlyDictionary<string, string> features,
        string key,
        int defaultValue,
        string? fallbackKey = null)
    {
        if (features.TryGetValue(key, out var raw) && int.TryParse(raw, out var value))
        {
            return value;
        }

        if (fallbackKey != null
            && features.TryGetValue(fallbackKey, out var fallback)
            && int.TryParse(fallback, out var fallbackValue))
        {
            return fallbackValue;
        }

        return defaultValue;
    }
}
