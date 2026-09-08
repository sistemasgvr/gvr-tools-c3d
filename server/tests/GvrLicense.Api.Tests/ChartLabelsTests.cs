using GvrLicense.Domain.Formatting;
using Xunit;

namespace GvrLicense.Api.Tests;

public class ChartLabelsTests
{
    [Fact]
    public void FormatMonthYear_UsesSpanishAbbreviation() =>
        Assert.Equal("ago 2026", ChartLabels.FormatMonthYear(new DateOnly(2026, 8, 1)));
}
