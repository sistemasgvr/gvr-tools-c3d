using GvrLicense.Domain.Formatting;
using Xunit;

namespace GvrLicense.Api.Tests;

public class LimaTimeTests
{
    [Fact]
    public void ToLima_AppliesFixedMinusFiveOffset()
    {
        var utc = new DateTimeOffset(2026, 8, 28, 20, 0, 0, TimeSpan.Zero);

        var lima = utc.ToLima();

        Assert.Equal(TimeSpan.FromHours(-5), lima.Offset);
        Assert.Equal(15, lima.Hour);
    }

    // Este es exactamente el bug que tenía window.gvrAdmin.formatDate: recortaba el string ISO en
    // UTC sin convertir, así que un evento de madrugada en UTC (que en Lima todavía es el día
    // anterior) mostraba el día siguiente al real.
    [Fact]
    public void ToLima_RollsBackToPreviousCalendarDay_WhenUtcIsEarlyMorning()
    {
        var utc = new DateTimeOffset(2026, 8, 28, 2, 0, 0, TimeSpan.Zero);

        var lima = utc.ToLima();

        Assert.Equal(new DateOnly(2026, 8, 27), DateOnly.FromDateTime(lima.DateTime));
        Assert.Equal(21, lima.Hour);
    }

    [Fact]
    public void ToLima_NullableOverload_PassesThroughNull()
    {
        DateTimeOffset? utc = null;

        Assert.Null(utc.ToLima());
    }
}
