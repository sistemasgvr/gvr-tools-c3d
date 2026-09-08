namespace GvrLicense.Infrastructure.Signing;

/// <summary>
/// EasyPanel / Docker env vars often store PEM with literal <c>\n</c> instead of real newlines.
/// <see cref="System.Security.Cryptography.ECDsa.ImportFromPem"/> needs actual line breaks.
/// </summary>
public static class PemNormalizer
{
    public static string Normalize(string? pem)
    {
        if (string.IsNullOrWhiteSpace(pem))
        {
            return string.Empty;
        }

        var value = pem.Trim().Trim('"').Trim('\'');

        // Env UI: "-----BEGIN...-----\nMHc...\n-----END...-----"
        if (value.Contains("\\n", StringComparison.Ordinal))
        {
            value = value.Replace("\\r\\n", "\n", StringComparison.Ordinal)
                .Replace("\\n", "\n", StringComparison.Ordinal);
        }

        value = value.Replace("\r\n", "\n", StringComparison.Ordinal);

        return value;
    }
}
