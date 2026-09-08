using System.Text.RegularExpressions;

namespace GvrLicense.Domain.Validation;

/// <summary>
/// Validación compartida de correos en activación y admin.
/// </summary>
public static partial class EmailValidator
{
    public const int MaxLength = 254;

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$", RegexOptions.CultureInvariant | RegexOptions.Compiled)]
    private static partial Regex EmailPattern();

    public static bool TryNormalize(string? input, out string normalized, out string error)
    {
        normalized = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(input))
        {
            error = "El correo es obligatorio.";
            return false;
        }

        var trimmed = input.Trim();
        if (trimmed.Length > MaxLength)
        {
            error = "El correo es demasiado largo.";
            return false;
        }

        if (!EmailPattern().IsMatch(trimmed))
        {
            error = "El correo no tiene un formato válido (ejemplo: nombre@empresa.com).";
            return false;
        }

        normalized = trimmed.ToLowerInvariant();
        return true;
    }
}
