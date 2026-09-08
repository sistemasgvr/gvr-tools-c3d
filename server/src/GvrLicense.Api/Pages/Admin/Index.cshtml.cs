using GvrLicense.Domain.Audit;
using GvrLicense.Domain.Entities;
using GvrLicense.Domain.Formatting;
using GvrLicense.Domain.LicenseKeys;
using GvrLicense.Infrastructure;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GvrLicense.Api.Pages.Admin;

public class IndexModel(LicenseDbContext db) : PageModel
{
    private const int RecentCount = 15;
    private const int TopUsageCount = 15;
    private const int RecentAuditCount = 20;
    private const string SheetsFeature = "quota.sheets_per_month";

    public List<LicenseRow> RecentRows { get; private set; } = [];
    public List<UsageRankRow> TopUsageRows { get; private set; } = [];
    public List<LicenseRow> UpcomingExpiryRows { get; private set; } = [];
    public List<AuditRow> RecentAuditRows { get; private set; } = [];

    public int ActiveCount { get; private set; }
    public int SuspendedCount { get; private set; }
    public int ExpiringSoonCount { get; private set; }
    public int CustomerCount { get; private set; }
    public int DeviceCount { get; private set; }
    public int ActiveSeatCount { get; private set; }
    public int SheetsThisMonth { get; private set; }
    public int PlanCount { get; private set; }
    public int NewQuoteCount { get; private set; }

    public IReadOnlyList<string> DailyChartLabels { get; private set; } = [];
    public IReadOnlyList<int> DailyChartValues { get; private set; } = [];
    public IReadOnlyList<string> MonthlyChartLabels { get; private set; } = [];
    public IReadOnlyList<int> MonthlyChartValues { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var utcNow = DateTime.UtcNow;
        var currentPeriod = new DateOnly(utcNow.Year, utcNow.Month, 1);
        var monthStart = new DateTimeOffset(currentPeriod.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var soonCutoff = DateTimeOffset.UtcNow.AddDays(30);

        var allLicenses = await db.Licenses
            .Include(l => l.Customer)
            .Include(l => l.Plan)
            .Select(l => new
            {
                l.Id,
                l.Key,
                CustomerName = l.Customer!.CompanyName,
                PlanCode = l.Plan!.Code,
                l.Status,
                l.ValidUntil,
                l.CreatedAtUtc
            })
            .ToListAsync();

        RecentRows = allLicenses
            .OrderByDescending(l => l.CreatedAtUtc)
            .Take(RecentCount)
            .Select(l => ToLicenseRow(l.Id, l.Key, l.CustomerName, l.PlanCode, l.Status, l.ValidUntil))
            .ToList();

        ExpiringSoonCount = allLicenses.Count(l =>
            l.Status == LicenseStatus.Active && l.ValidUntil <= soonCutoff);

        UpcomingExpiryRows = allLicenses
            .Where(l => l.Status == LicenseStatus.Active)
            .OrderBy(l => l.ValidUntil)
            .Take(RecentCount)
            .Select(l => ToLicenseRow(l.Id, l.Key, l.CustomerName, l.PlanCode, l.Status, l.ValidUntil))
            .ToList();

        ActiveCount = allLicenses.Count(l => l.Status == LicenseStatus.Active);
        SuspendedCount = allLicenses.Count(l => l.Status == LicenseStatus.Suspended);
        CustomerCount = await db.Customers.CountAsync();
        DeviceCount = await db.Devices.CountAsync();
        PlanCount = await db.Plans.CountAsync(p => p.IsActive);
        ActiveSeatCount = await db.Devices.Select(d => d.CompanyUserId).Distinct().CountAsync();
        NewQuoteCount = await db.QuoteRequests.CountAsync(q => q.Status == QuoteRequestStatus.New);

        SheetsThisMonth = await db.UsageCounters
            .Where(u => u.Period == currentPeriod && u.FeatureCode == SheetsFeature)
            .Select(u => (int?)u.Consumed)
            .SumAsync() ?? 0;

        var topCounters = await db.UsageCounters
            .AsNoTracking()
            .Where(u => u.Period == currentPeriod && u.FeatureCode == SheetsFeature && u.Consumed > 0)
            .OrderByDescending(u => u.Consumed)
            .Take(TopUsageCount)
            .ToListAsync();

        if (topCounters.Count == 0)
        {
            TopUsageRows = [];
        }
        else
        {
            var licenseIds = topCounters.Select(c => c.LicenseId).ToList();
            var licensesById = await db.Licenses
                .AsNoTracking()
                .Include(l => l.Customer)
                .Where(l => licenseIds.Contains(l.Id))
                .ToDictionaryAsync(l => l.Id);

            TopUsageRows = topCounters
                .Where(c => licensesById.ContainsKey(c.LicenseId))
                .Select(c =>
                {
                    var license = licensesById[c.LicenseId];
                    return new UsageRankRow(
                        license.Id,
                        LicenseKeyGenerator.FormatForDisplay(license.Key),
                        license.Customer!.CompanyName,
                        c.Consumed,
                        c.QuotaLimit);
                })
                .ToList();
        }

        var usageEvents = await db.UsageEvents
            .AsNoTracking()
            .Where(e => e.FeatureCode == SheetsFeature && e.OccurredAtUtc >= monthStart)
            .Select(e => new { e.OccurredAtUtc, e.Quantity })
            .ToListAsync();

        var daysInMonth = DateTime.DaysInMonth(utcNow.Year, utcNow.Month);
        var dailyPeriods = Enumerable.Range(1, daysInMonth)
            .Select(day => new DateOnly(utcNow.Year, utcNow.Month, day))
            .ToList();

        DailyChartLabels = dailyPeriods.Select(d => d.ToString("dd/MM")).ToList();
        DailyChartValues = dailyPeriods
            .Select(day => usageEvents
                .Where(e => DateOnly.FromDateTime(e.OccurredAtUtc.UtcDateTime) == day)
                .Sum(e => e.Quantity))
            .ToList();

        var monthlyPeriods = Enumerable.Range(0, 6)
            .Select(i =>
            {
                var dt = utcNow.AddMonths(-5 + i);
                return new DateOnly(dt.Year, dt.Month, 1);
            })
            .ToList();

        var monthlyTotals = await db.UsageCounters
            .AsNoTracking()
            .Where(u => u.FeatureCode == SheetsFeature && monthlyPeriods.Contains(u.Period))
            .GroupBy(u => u.Period)
            .Select(g => new { Period = g.Key, Total = g.Sum(x => x.Consumed) })
            .ToListAsync();

        MonthlyChartLabels = monthlyPeriods
            .Select(ChartLabels.FormatMonthYear)
            .ToList();
        MonthlyChartValues = monthlyPeriods
            .Select(p => monthlyTotals.FirstOrDefault(t => t.Period == p)?.Total ?? 0)
            .ToList();

        var recentAudit = await db.AuditLogs
            .AsNoTracking()
            .OrderByDescending(a => a.OccurredAtUtc)
            .Take(RecentAuditCount)
            .Select(a => new { a.Id, a.Actor, a.Action, a.DetailsJson, a.OccurredAtUtc, a.LicenseId })
            .ToListAsync();

        RecentAuditRows = recentAudit
            .Select(a => new AuditRow(
                a.Id,
                a.Actor,
                a.Action,
                AuditActionDescriber.Describe(a.Action, a.DetailsJson),
                a.OccurredAtUtc,
                a.LicenseId))
            .ToList();
    }

    private static LicenseRow ToLicenseRow(
        Guid id,
        string key,
        string customerName,
        string planCode,
        LicenseStatus status,
        DateTimeOffset validUntil) =>
        new(
            id,
            LicenseKeyGenerator.FormatForDisplay(key),
            customerName,
            planCode,
            status,
            validUntil,
            DaysUntil(validUntil));

    public static int DaysUntil(DateTimeOffset validUntil) =>
        Math.Max(0, (int)Math.Ceiling((validUntil - DateTimeOffset.UtcNow).TotalDays));

    public sealed record LicenseRow(
        Guid Id,
        string Key,
        string CustomerName,
        string PlanCode,
        LicenseStatus Status,
        DateTimeOffset ValidUntil,
        int DaysUntil);

    public sealed record UsageRankRow(
        Guid LicenseId,
        string Key,
        string CustomerName,
        int Consumed,
        int QuotaLimit);

    public sealed record AuditRow(
        Guid Id,
        string Actor,
        string Action,
        string ActionLabel,
        DateTimeOffset OccurredAtUtc,
        Guid? LicenseId);
}
