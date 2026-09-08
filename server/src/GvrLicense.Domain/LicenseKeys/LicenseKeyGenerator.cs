using System.Security.Cryptography;
using System.Text;

namespace GvrLicense.Domain.LicenseKeys;

/// <summary>
/// Genera y valida el formato GVR-XXXX-XXXX-XXXX:
/// 10 símbolos de payload aleatorio + 2 símbolos de checksum (CRC-16/CCITT-FALSE), todo en Crockford
/// Base32. El checksum deja rechazar un typo del cliente sin tocar la base de datos; no es
/// criptográfico, solo detección de error.
/// </summary>
public static class LicenseKeyGenerator
{
    // Crockford Base32: sin I, L, O, U (se confunden con 1/1/0 al dictarla, U se evita por otras razones).
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const int PayloadLength = 10;

    public static string Generate()
    {
        // 7 bytes = 56 bits; los primeros 10 símbolos base32 consumen exactamente 50 de esos bits.
        Span<byte> payload = stackalloc byte[7];
        RandomNumberGenerator.Fill(payload);

        var body = EncodeBase32(payload)[..PayloadLength];
        var checksum = ComputeChecksum(body);

        return $"GVR-{body[..4]}-{body[4..8]}-{body[8..10]}{checksum}";
    }

    /// <summary>
    /// Normaliza lo que pega el usuario (espacios, minúsculas) a GVR-XXXX-XXXX-XXXX.
    /// </summary>
    public static string Normalize(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        var normalized = key.Trim().ToUpperInvariant().Replace(' ', '-');
        while (normalized.Contains("--", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("--", "-", StringComparison.Ordinal);
        }

        return normalized;
    }

    /// <summary>Presentación uniforme en admin (GVR-XXXX-XXXX-XXXX).</summary>
    public static string FormatForDisplay(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        var normalized = Normalize(key);
        if (normalized.StartsWith("GVR-", StringComparison.Ordinal))
        {
            return normalized;
        }

        var compact = normalized.Replace("-", string.Empty, StringComparison.Ordinal);
        if (compact.Length == PayloadLength + 2)
        {
            return $"GVR-{compact[..4]}-{compact[4..8]}-{compact[8..]}";
        }

        return normalized;
    }

    /// <summary>Valida formato + checksum, sin tocar la base de datos. No confirma que la key exista.</summary>
    public static bool TryValidateFormat(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var compact = Normalize(key);
        if (compact.StartsWith("GVR-", StringComparison.Ordinal))
        {
            compact = compact["GVR-".Length..];
        }

        compact = compact.Replace("-", string.Empty).Replace(" ", string.Empty);

        if (compact.Length != PayloadLength + 2)
        {
            return false;
        }

        var body = compact[..PayloadLength];
        var checksum = compact[PayloadLength..];

        return body.IndexOfAny(InvalidChars) < 0
               && checksum.IndexOfAny(InvalidChars) < 0
               && string.Equals(ComputeChecksum(body), checksum, StringComparison.Ordinal);
    }

    private static readonly char[] InvalidChars = "ILOU".ToCharArray();

    private static string EncodeBase32(ReadOnlySpan<byte> data)
    {
        var sb = new StringBuilder();
        int bitBuffer = 0;
        int bitsInBuffer = 0;

        foreach (var b in data)
        {
            bitBuffer = (bitBuffer << 8) | b;
            bitsInBuffer += 8;

            while (bitsInBuffer >= 5)
            {
                bitsInBuffer -= 5;
                var index = (bitBuffer >> bitsInBuffer) & 0b11111;
                sb.Append(Alphabet[index]);
            }
        }

        if (bitsInBuffer > 0)
        {
            var index = (bitBuffer << (5 - bitsInBuffer)) & 0b11111;
            sb.Append(Alphabet[index]);
        }

        return sb.ToString();
    }

    private static string ComputeChecksum(string body)
    {
        ushort crc = 0xFFFF;
        foreach (var c in body)
        {
            crc ^= (ushort)(c << 8);
            for (var i = 0; i < 8; i++)
            {
                crc = (crc & 0x8000) != 0 ? (ushort)((crc << 1) ^ 0x1021) : (ushort)(crc << 1);
            }
        }

        var bits10 = crc & 0x3FF;
        var c1 = Alphabet[(bits10 >> 5) & 0b11111];
        var c2 = Alphabet[bits10 & 0b11111];
        return $"{c1}{c2}";
    }
}
