using GvrLicense.Domain.LicenseKeys;
using Xunit;

namespace GvrLicense.Api.Tests;

public class LicenseKeyGeneratorTests
{
    [Fact]
    public void Generate_ProducesExpectedFormat()
    {
        var key = LicenseKeyGenerator.Generate();

        Assert.Matches(@"^GVR-[0-9A-Z]{4}-[0-9A-Z]{4}-[0-9A-Z]{4}$", key);
    }

    [Fact]
    public void Generate_RoundTripsThroughValidation()
    {
        for (var i = 0; i < 200; i++)
        {
            var key = LicenseKeyGenerator.Generate();
            Assert.True(LicenseKeyGenerator.TryValidateFormat(key), $"'{key}' debería validar.");
        }
    }

    [Fact]
    public void TryValidateFormat_RejectsSingleCharTypo()
    {
        var key = LicenseKeyGenerator.Generate();
        var tampered = ReplaceLastChar(key);

        Assert.False(LicenseKeyGenerator.TryValidateFormat(tampered));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("GVR-IIII-LLLL-OUOU")]
    [InlineData("not-a-key")]
    public void TryValidateFormat_RejectsGarbage(string? input)
    {
        Assert.False(LicenseKeyGenerator.TryValidateFormat(input));
    }

    [Fact]
    public void FormatForDisplay_NormalizesLegacyKeyWithoutPrefix()
    {
        Assert.Equal("GVR-N7ZD-SMZD-NTV5", LicenseKeyGenerator.FormatForDisplay("N7ZD-SMZD-NTV5"));
    }

    [Fact]
    public void FormatForDisplay_NormalizesSpacesToDashes()
    {
        Assert.Equal("GVR-2N2E-802D-VZPX", LicenseKeyGenerator.FormatForDisplay("GVR 2N2E 802D VZPX"));
    }

    private static string ReplaceLastChar(string key)
    {
        var lastChar = key[^1];
        var replacement = lastChar == '0' ? '2' : '0';
        return key[..^1] + replacement;
    }
}
