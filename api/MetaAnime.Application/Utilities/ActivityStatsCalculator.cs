using MetaAnime.Application.Contracts;
using MetaAnime.Domain.Entities;

namespace MetaAnime.Application.Utilities;

/// <summary>
/// Pure aggregation of mirrored Sonarr import events into the activity-stats payload.
/// Kept separate from the mediator so the day/week/month/year bucketing (and its
/// timezone handling) can be unit-tested without a database or an HTTP client.
/// </summary>
public static class ActivityStatsCalculator
{
    /// <summary>
    /// Days of slack kept before January 1st: the weekly chart reaches back six days before
    /// today, and a timezone shift can move an event into the previous calendar year.
    /// </summary>
    public const int WindowSlackDays = 8;

    private static readonly string[] ShortDays = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
    private static readonly string[] ShortMonths =
        ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    /// <summary>
    /// Earliest UTC instant the stats need. Everything older can be pruned from the mirror.
    /// </summary>
    public static DateTime WindowStartUtc(DateTime nowUtc) =>
        new DateTime(nowUtc.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(-WindowSlackDays);

    /// <summary>
    /// Local-midnight boundaries of every selectable period. Shared by the tiles aggregation
    /// and the episode-list query so the two can never disagree about what "this week" is.
    /// </summary>
    private readonly record struct LocalBounds(DateTime Today, DateTime Week, DateTime Month, DateTime Year);

    private static LocalBounds Bounds(DateTime nowLocal) => new(
        nowLocal.Date,
        // Weeks start on MONDAY (ISO 8601 / French calendar). The Sunday-based start made
        // "This Week" collapse to a single day every Sunday, which reads as a broken card.
        nowLocal.Date.AddDays(-(((int)nowLocal.DayOfWeek + 6) % 7)),
        new DateTime(nowLocal.Year, nowLocal.Month, 1),
        new DateTime(nowLocal.Year, 1, 1));

    /// <summary>
    /// UTC instant at/after which an event belongs to <paramref name="period"/> for a caller at
    /// <paramref name="tzOffsetMinutes"/>.
    /// </summary>
    public static DateTime PeriodStartUtc(ActivityStatsPeriod period, DateTime nowUtc, int tzOffsetMinutes)
    {
        var bounds = Bounds(nowUtc.AddMinutes(-tzOffsetMinutes));
        var localStart = period switch
        {
            ActivityStatsPeriod.Today     => bounds.Today,
            ActivityStatsPeriod.ThisWeek  => bounds.Week,
            ActivityStatsPeriod.ThisMonth => bounds.Month,
            _                             => bounds.Year,
        };
        // local = utc − offset  ⇒  utc = local + offset. The result is a UTC *instant* (the
        // wall-clock value happens to be local midnight), so Npgsql must see Kind=Utc.
        return DateTime.SpecifyKind(localStart.AddMinutes(tzOffsetMinutes), DateTimeKind.Utc);
    }

    /// <param name="events">Mirrored import events (any order, any span).</param>
    /// <param name="nowUtc">Current UTC time; injected for testability.</param>
    /// <param name="tzOffsetMinutes">
    /// JavaScript <c>Date.getTimezoneOffset()</c> convention: minutes of (UTC − local), so
    /// UTC-5 → 300. Subtracting it from UTC yields the caller's wall clock.
    /// </param>
    public static ActivityStatsResponse Build(
        IReadOnlyCollection<SonarrHistoryEvent> events,
        DateTime nowUtc,
        int tzOffsetMinutes)
    {
        var nowLocal   = nowUtc.AddMinutes(-tzOffsetMinutes);
        var bounds     = Bounds(nowLocal);
        var todayStart = bounds.Today;
        var weekStart  = bounds.Week;
        var monthStart = bounds.Month;
        var yearStart  = bounds.Year;

        // Single pass over the mirror: bucket once per local day. The previous implementation
        // rescanned the whole event list ~50 times, re-running the timezone shift on every
        // element of every pass (2.5M DateTime.AddMinutes calls on a 50k-event history).
        var perDay = new Dictionary<DateTime, (int Count, long Bytes)>(512);
        foreach (var evt in events)
        {
            var day = evt.OccurredAt.AddMinutes(-tzOffsetMinutes).Date;
            if (day > todayStart)
                continue; // future-dated rows can never belong to a closed period

            perDay.TryGetValue(day, out var bucket);
            perDay[day] = (bucket.Count + 1, bucket.Bytes + evt.SizeBytes);
        }

        // ── Period totals: one walk over the buckets (≤ 366 entries) ────────────
        int  dlToday = 0, dlWeek = 0, dlMonth = 0, dlYear = 0;
        long stToday = 0, stWeek = 0, stMonth = 0, stYear = 0;

        // Independent checks, deliberately NOT a `continue` chain: a week that straddles a
        // month boundary (e.g. Mon Oct 26 → Sun Nov 1) must keep its earlier days in the
        // weekly total even though they belong to the previous month.
        foreach (var (day, bucket) in perDay)
        {
            if (day >= yearStart)
            {
                dlYear += bucket.Count; stYear += bucket.Bytes;
            }

            if (day >= monthStart)
            {
                dlMonth += bucket.Count; stMonth += bucket.Bytes;
            }

            if (day >= weekStart)
            {
                dlWeek += bucket.Count; stWeek += bucket.Bytes;
            }

            if (day == todayStart)
            {
                dlToday += bucket.Count; stToday += bucket.Bytes;
            }
        }

        // ── Chart series ───────────────────────────────────────────────────────
        // Week: the CURRENT calendar week, Monday → Sunday — the same window the
        // "This Week" counter and the drill-down list use. It used to plot a rolling
        // "last 7 days" window, so on a Sunday the chart showed six days that the tile
        // excluded (chart 78 episodes vs tile 6). Future days report zero, exactly like
        // the month chart.
        var weekPoints = new List<ActivityChartPoint>(7);
        for (var i = 0; i < 7; i++)
        {
            var day = weekStart.AddDays(i);
            perDay.TryGetValue(day, out var bucket);
            weekPoints.Add(new ActivityChartPoint(ShortDays[(int)day.DayOfWeek], bucket.Count, bucket.Bytes));
        }

        // Month: every day of the current month (days still to come report zero).
        var daysInMonth = DateTime.DaysInMonth(nowLocal.Year, nowLocal.Month);
        var monthPoints = new List<ActivityChartPoint>(daysInMonth);
        for (var d = 1; d <= daysInMonth; d++)
        {
            perDay.TryGetValue(new DateTime(nowLocal.Year, nowLocal.Month, d), out var bucket);
            monthPoints.Add(new ActivityChartPoint(d.ToString(), bucket.Count, bucket.Bytes));
        }

        // Year: all 12 months of the current year, accumulated from the same buckets.
        var monthlyCount = new int[13];
        var monthlyBytes = new long[13];
        foreach (var (day, bucket) in perDay)
        {
            if (day.Year != nowLocal.Year)
                continue;
            monthlyCount[day.Month] += bucket.Count;
            monthlyBytes[day.Month] += bucket.Bytes;
        }

        var yearPoints = new List<ActivityChartPoint>(12);
        for (var m = 1; m <= 12; m++)
            yearPoints.Add(new ActivityChartPoint(ShortMonths[m - 1], monthlyCount[m], monthlyBytes[m]));

        return new ActivityStatsResponse(
            new ActivityPeriodStats(dlToday, dlWeek, dlMonth, dlYear),
            new StoragePeriodStats(stToday, stWeek, stMonth, stYear),
            new ActivityChartData(weekPoints, monthPoints, yearPoints));
    }
}
