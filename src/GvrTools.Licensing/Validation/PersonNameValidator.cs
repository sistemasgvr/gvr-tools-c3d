using System;

namespace GvrTools.Licensing.Validation
{
    public static class PersonNameValidator
    {
        public static bool TryNormalize(string input, out string normalized, out string error)
        {
            normalized = string.Empty;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(input))
            {
                error = "El nombre es obligatorio.";
                return false;
            }

            normalized = input.Trim();
            if (normalized.Length < 2)
            {
                error = "El nombre debe tener al menos 2 caracteres.";
                return false;
            }

            if (normalized.Length > 120)
            {
                error = "El nombre es demasiado largo.";
                return false;
            }

            return true;
        }
    }
}
