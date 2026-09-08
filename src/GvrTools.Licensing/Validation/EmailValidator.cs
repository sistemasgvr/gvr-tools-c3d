using System;
using System.Text.RegularExpressions;

namespace GvrTools.Licensing.Validation
{
    public static class EmailValidator
    {
        private static readonly Regex Pattern = new Regex(
            @"^[^\s@]+@[^\s@]+\.[^\s@]+$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static bool TryNormalize(string input, out string normalized, out string error)
        {
            normalized = string.Empty;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(input))
            {
                error = "El correo es obligatorio.";
                return false;
            }

            string trimmed = input.Trim();
            if (trimmed.Length > 254)
            {
                error = "El correo es demasiado largo.";
                return false;
            }

            if (!Pattern.IsMatch(trimmed))
            {
                error = "El correo no tiene un formato válido (ejemplo: nombre@empresa.com).";
                return false;
            }

            normalized = trimmed.ToLowerInvariant();
            return true;
        }
    }
}
