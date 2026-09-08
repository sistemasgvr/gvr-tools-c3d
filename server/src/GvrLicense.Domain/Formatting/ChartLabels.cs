namespace GvrLicense.Domain.Formatting;

public static class ChartLabels
{
    private static readonly string[] SpanishMonths =
        ["ene", "feb", "mar", "abr", "may", "jun", "jul", "ago", "sep", "oct", "nov", "dic"];

    public static string FormatMonthYear(DateOnly period) =>
        $"{SpanishMonths[period.Month - 1]} {period.Year}";
}
