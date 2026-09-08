namespace GvrLicense.Domain.Validation;

public static class PersonNameValidator
{
    public const int MinLength = 2;
    public const int MaxLength = 120;

    public static bool TryNormalize(string? input, out string normalized, out string error)
    {
        normalized = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(input))
        {
            error = "El nombre es obligatorio.";
            return false;
        }

        normalized = input.Trim();
        if (normalized.Length < MinLength)
        {
            error = $"El nombre debe tener al menos {MinLength} caracteres.";
            return false;
        }

        if (normalized.Length > MaxLength)
        {
            error = "El nombre es demasiado largo.";
            return false;
        }

        return true;
    }
}
