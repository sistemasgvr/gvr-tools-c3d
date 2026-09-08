using System.Globalization;
using System.Text.RegularExpressions;

namespace GvrLicense.Domain.Versioning;

/// <summary>A small SemVer parser/comparer for release selection and validation.</summary>
public sealed class SemVersion : IComparable<SemVersion>
{
    private static readonly Regex VersionPattern = new(
        @"^[vV]?(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:-(?<prerelease>[0-9A-Za-z.-]+))?(?:\+[0-9A-Za-z.-]+)?$",
        RegexOptions.CultureInvariant);

    private SemVersion(int major, int minor, int patch, string? prerelease)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = prerelease;
    }

    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }
    public string? Prerelease { get; }

    public static bool TryParse(string? value, out SemVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var match = VersionPattern.Match(value.Trim());
        if (!match.Success
            || !int.TryParse(match.Groups["major"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            || !int.TryParse(match.Groups["minor"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var minor)
            || !int.TryParse(match.Groups["patch"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var patch))
        {
            return false;
        }

        var prerelease = match.Groups["prerelease"].Success ? match.Groups["prerelease"].Value : null;
        if (prerelease?.Split('.').Any(string.IsNullOrEmpty) == true)
        {
            return false;
        }

        version = new SemVersion(major, minor, patch, prerelease);
        return true;
    }

    public static string Normalize(string value)
    {
        if (!TryParse(value, out var version))
        {
            throw new FormatException($"'{value}' no es una versión SemVer válida.");
        }

        return version!.ToString();
    }

    public static int Compare(SemVersion left, SemVersion right) => left.CompareTo(right);

    public static int Compare(string left, string right) =>
        Parse(left).CompareTo(Parse(right));

    public static bool IsGreaterThan(string candidate, string current) =>
        Compare(candidate, current) > 0;

    public bool IsGreaterThan(SemVersion other) => CompareTo(other) > 0;

    /// <summary>Highest parseable SemVer in the list, or null if none.</summary>
    public static SemVersion? MaxOf(IEnumerable<string?> versions)
    {
        SemVersion? max = null;
        foreach (var value in versions)
        {
            if (!TryParse(value, out var parsed) || parsed is null)
            {
                continue;
            }

            if (max is null || parsed.IsGreaterThan(max))
            {
                max = parsed;
            }
        }

        return max;
    }

    public int CompareTo(SemVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var result = Major.CompareTo(other.Major);
        if (result != 0) return result;
        result = Minor.CompareTo(other.Minor);
        if (result != 0) return result;
        result = Patch.CompareTo(other.Patch);
        if (result != 0) return result;

        if (Prerelease is null) return other.Prerelease is null ? 0 : 1;
        if (other.Prerelease is null) return -1;

        var leftIdentifiers = Prerelease.Split('.');
        var rightIdentifiers = other.Prerelease.Split('.');
        for (var i = 0; i < Math.Min(leftIdentifiers.Length, rightIdentifiers.Length); i++)
        {
            result = CompareIdentifier(leftIdentifiers[i], rightIdentifiers[i]);
            if (result != 0) return result;
        }

        return leftIdentifiers.Length.CompareTo(rightIdentifiers.Length);
    }

    public override string ToString() =>
        $"{Major}.{Minor}.{Patch}{(Prerelease is null ? string.Empty : $"-{Prerelease}")}";

    private static SemVersion Parse(string value) =>
        TryParse(value, out var version)
            ? version!
            : throw new FormatException($"'{value}' no es una versión SemVer válida.");

    private static int CompareIdentifier(string left, string right)
    {
        var leftNumeric = left.All(char.IsDigit);
        var rightNumeric = right.All(char.IsDigit);
        if (leftNumeric && rightNumeric)
        {
            var normalizedLeft = left.TrimStart('0');
            var normalizedRight = right.TrimStart('0');
            normalizedLeft = normalizedLeft.Length == 0 ? "0" : normalizedLeft;
            normalizedRight = normalizedRight.Length == 0 ? "0" : normalizedRight;
            var lengthResult = normalizedLeft.Length.CompareTo(normalizedRight.Length);
            return lengthResult != 0
                ? lengthResult
                : string.CompareOrdinal(normalizedLeft, normalizedRight);
        }

        if (leftNumeric) return -1;
        if (rightNumeric) return 1;
        return string.CompareOrdinal(left, right);
    }
}
