using FluentAssertions;
using MetaAnime.Application.Contracts;
using MetaAnime.Application.Utilities;
using MetaAnime.Domain.Entities;

namespace MetaAnime.UnitTests.Services;

/// <summary>
/// Pins the activity-stats bucketing: the endpoint's numbers are only trustworthy if the
/// local-day boundaries, the tz shift and the "future periods report zero" shape hold.
/// </summary>
public class ActivityStatsCalculatorTests
{
    private static long _nextId = 1;

    private static SonarrHistoryEvent Event(DateTime utc, long sizeBytes = 100)
        => new() { Id = _nextId++, OccurredAt = utc, SizeBytes = sizeBytes };

    // Wednesday 2026-11-04 12:00 UTC.
    private static readonly DateTime Now = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Build_ShiftsEventsIntoTheCallersLocalDay()
    {
        // 2026-11-03T03:00Z is still Nov 2nd for a UTC-5 caller (tzOffset = 300)
        // and Nov 3rd for a UTC caller. Same event, different bucket — the offsets must win.
        var events = new[] { Event(new DateTime(2026, 11, 3, 3, 0, 0, DateTimeKind.Utc), 500) };

        var utc = ActivityStatsCalculator.Build(events, Now, tzOffsetMinutes: 0);
        var est = ActivityStatsCalculator.Build(events, Now, tzOffsetMinutes: 300);

        // Nov 2nd bucket in both cases, but only EST counts it as "yesterday": the UTC
        // week chart labels it Tue, the EST one Mon.
        utc.Chart.Week.Single(p => p.Label == "Tue").Downloads.Should().Be(1);
        est.Chart.Week.Single(p => p.Label == "Tue").Downloads.Should().Be(0);
        est.Chart.Week.Single(p => p.Label == "Mon").Downloads.Should().Be(1);
    }

    [Fact]
    public void Build_SecondLocalDayBoundary_CountsTowardToday()
    {
        // 2026-11-04T04:30Z = Nov 3rd 23:30 in UTC-5 … one minute before local midnight.
        var before = Event(new DateTime(2026, 11, 4, 4, 59, 0, DateTimeKind.Utc), 10);
        // 2026-11-04T05:00Z = Nov 4th 00:00 local.
        var after = Event(new DateTime(2026, 11, 4, 5, 0, 0, DateTimeKind.Utc), 90);

        var stats = ActivityStatsCalculator.Build([before, after], Now, tzOffsetMinutes: 300);

        stats.Downloads.Today.Should().Be(1, "only the event after local midnight belongs to today");
        stats.Storage.Today.Should().Be(90);
        stats.Downloads.ThisMonth.Should().Be(2);
        stats.Storage.ThisMonth.Should().Be(100);
    }

    [Fact]
    public void Build_ComputesPeriodTotalsAndIgnoresEarlierYears()
    {
        var events = new[]
        {
            Event(new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc), 1_000), // today, this week, month, year
            Event(new DateTime(2026, 11, 2, 10, 0, 0, DateTimeKind.Utc), 2_000), // this week (Mon), month, year
            Event(new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc), 4_000), // same ISO week (Sun) — Sum(a)-based week
            Event(new DateTime(2026, 10, 15, 10, 0, 0, DateTimeKind.Utc), 8_000), // month/year only
            Event(new DateTime(2025, 12, 31, 10, 0, 0, DateTimeKind.Utc), 16_000), // previous year — excluded
        };

        var stats = ActivityStatsCalculator.Build(events, Now, tzOffsetMinutes: 0);

        stats.Downloads.Today.Should().Be(1);
        stats.Downloads.ThisWeek.Should().Be(2);   // Monday-based week start: Nov 2 → Nov 4
        stats.Downloads.ThisMonth.Should().Be(3);  // Oct 15 belongs to the year, not the month
        stats.Downloads.ThisYear.Should().Be(4);
        stats.Storage.Today.Should().Be(1_000);
        stats.Storage.ThisWeek.Should().Be(3_000);  // Nov 4 (1 000) + Nov 2 (2 000)
        stats.Storage.ThisMonth.Should().Be(7_000);
        stats.Storage.ThisYear.Should().Be(15_000);
    }

    [Fact]
    public void Build_ProducesFullChartShapes_WithFutureBucketsAtZero()
    {
        var stats = ActivityStatsCalculator.Build([Event(new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc), 777)], Now, 0);

        stats.Chart.Week.Should().HaveCount(7);
        // Calendar week Monday → Sunday: index 2 is this Wednesday (today).
        stats.Chart.Week.Select(p => p.Label).Should().Equal("Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun");
        stats.Chart.Week[2].Downloads.Should().Be(1);
        stats.Chart.Week[3].Downloads.Should().Be(0, "future days still render a zero point");

        stats.Chart.Month.Should().HaveCount(30, "November has 30 days — every day is present");
        stats.Chart.Month[3].Label.Should().Be("4");
        stats.Chart.Month[3].StorageBytes.Should().Be(777);
        stats.Chart.Month[29].Downloads.Should().Be(0, "future days still render a zero point");

        stats.Chart.Year.Should().HaveCount(12);
        stats.Chart.Year[10].Label.Should().Be("Nov");
        stats.Chart.Year[10].Downloads.Should().Be(1);
        stats.Chart.Year[11].Downloads.Should().Be(0);
    }

    [Fact]
    public void Build_EmptyMirror_ReturnsZeroesWithChartShape()
    {
        var stats = ActivityStatsCalculator.Build([], Now, 300);

        stats.Downloads.Should().Be(new MetaAnime.Application.Contracts.ActivityPeriodStats(0, 0, 0, 0));
        stats.Chart.Week.Should().HaveCount(7);
        stats.Chart.Month.Should().HaveCount(30);
        stats.Chart.Year.Should().HaveCount(12);
    }

    [Fact]
    public void Build_CountsDuplicateEventsIndividually()
    {
        // The mirror is keyed by Sonarr history id, so two different imports of the same
        // file size are two downloads — only bucket counts, never de-duplication by value.
        var events = new[]
        {
            Event(new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc), 500),
            Event(new DateTime(2026, 11, 4, 11, 0, 0, DateTimeKind.Utc), 500),
        };

        var stats = ActivityStatsCalculator.Build(events, Now, 0);

        stats.Downloads.Today.Should().Be(2);
        stats.Storage.Today.Should().Be(1_000);
    }

    [Fact]
    public void WindowStartUtc_KeepsSlackForTheWeeklyChartAndTimezones()
    {
        var start = ActivityStatsCalculator.WindowStartUtc(Now);

        start.Should().Be(new DateTime(2025, 12, 24, 0, 0, 0, DateTimeKind.Utc));
        // The 7-day chart can reach back to Dec 26 for a Jan 1 "today"; the extra slack
        // also covers a timezone shift moving an event into the previous year.
        start.Should().BeBefore(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Build_WeekChartWindowMatchesTheThisWeekCounter()
    {
        // Regression: the week chart used to plot a rolling "last 7 days" window while the
        // counter counted the calendar week, so on a Sunday the chart showed six days the
        // counter excluded (78 episodes on the chart vs 6 in the tile).
        var events = new[]
        {
            Event(new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc)),  // this week (Wed)
            Event(new DateTime(2026, 10, 28, 10, 0, 0, DateTimeKind.Utc)), // previous week — chart must NOT show it
            Event(new DateTime(2026, 10, 29, 10, 0, 0, DateTimeKind.Utc)), // previous week
        };

        var stats = ActivityStatsCalculator.Build(events, Now, tzOffsetMinutes: 0);

        stats.Downloads.ThisWeek.Should().Be(1);
        stats.Chart.Week.Sum(p => p.Downloads).Should().Be(stats.Downloads.ThisWeek);
        stats.Chart.Week.Sum(p => p.StorageBytes).Should().Be(stats.Storage.ThisWeek);
    }

    [Fact]
    public void Build_OnASunday_TheWeekStillCoversMondayToSunday()
    {
        // 2026-11-01 is a Sunday. A Sunday-based week would collapse to a single day (which
        // made Today == This Week == This Month and looked like a broken card); Monday-based
        // weeks keep the whole Oct 26 → Nov 1 window.
        var sundayNow = new DateTime(2026, 11, 1, 20, 0, 0, DateTimeKind.Utc);
        var events = new[]
        {
            Event(new DateTime(2026, 11, 1, 17, 0, 0, DateTimeKind.Utc)), // today (Sunday)
            Event(new DateTime(2026, 10, 31, 17, 0, 0, DateTimeKind.Utc)), // Saturday, same week
            Event(new DateTime(2026, 10, 25, 17, 0, 0, DateTimeKind.Utc)), // previous Sunday — out
        };

        var stats = ActivityStatsCalculator.Build(events, sundayNow, tzOffsetMinutes: 0);

        stats.Downloads.Today.Should().Be(1);
        stats.Downloads.ThisWeek.Should().Be(2);
        stats.Chart.Week.Should().HaveCount(7);
        stats.Chart.Week[^1].Label.Should().Be("Sun");
        stats.Chart.Week[^1].Downloads.Should().Be(1);
        stats.Chart.Week[5].Label.Should().Be("Sat");
        stats.Chart.Week[5].Downloads.Should().Be(1);
    }

    [Theory]
    // nowUtc 2026-11-04 12:00Z (Wednesday). UTC-5 caller: local "now" is 07:00 on Nov 4.
    [InlineData(ActivityStatsPeriod.Today,     0,   "2026-11-04T00:00:00Z")]
    [InlineData(ActivityStatsPeriod.ThisWeek,  0,   "2026-11-02T00:00:00Z")] // Monday
    [InlineData(ActivityStatsPeriod.ThisMonth, 0,   "2026-11-01T00:00:00Z")]
    [InlineData(ActivityStatsPeriod.ThisYear,  0,   "2026-01-01T00:00:00Z")]
    // Same instants shift by the offset: local midnight in UTC-5 is 05:00Z.
    [InlineData(ActivityStatsPeriod.Today,     300, "2026-11-04T05:00:00Z")]
    [InlineData(ActivityStatsPeriod.ThisMonth, 300, "2026-11-01T05:00:00Z")]
    [InlineData(ActivityStatsPeriod.ThisYear,  300, "2026-01-01T05:00:00Z")]
    public void PeriodStartUtc_ReturnsTheLocalMidnightOfThePeriod(
        ActivityStatsPeriod period, int tzOffsetMinutes, string expectedUtc)
    {
        var start = ActivityStatsCalculator.PeriodStartUtc(period, Now, tzOffsetMinutes);

        start.Should().Be(DateTime.Parse(expectedUtc, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal));
        // Npgsql refuses Unspecified/ Local kinds for timestamptz parameters — the value feeds
        // straight into the mirror query, so it must be typed UTC.
        start.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void PeriodStartUtc_AgreesWithTheTileCounts()
    {
        // The drill-down list and the counters must select the same events: every event at or
        // after PeriodStartUtc(period) must be counted by the tile aggregation for that period.
        var events = new[]
        {
            Event(new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc)),
            Event(new DateTime(2026, 11, 2, 10, 0, 0, DateTimeKind.Utc)),
            Event(new DateTime(2026, 10, 20, 10, 0, 0, DateTimeKind.Utc)),
        };

        var stats = ActivityStatsCalculator.Build(events, Now, tzOffsetMinutes: 0);

        int CountSince(ActivityStatsPeriod p) =>
            events.Count(e => e.OccurredAt >= ActivityStatsCalculator.PeriodStartUtc(p, Now, 0));

        CountSince(ActivityStatsPeriod.Today).Should().Be(stats.Downloads.Today);
        CountSince(ActivityStatsPeriod.ThisWeek).Should().Be(stats.Downloads.ThisWeek);
        CountSince(ActivityStatsPeriod.ThisMonth).Should().Be(stats.Downloads.ThisMonth);
        CountSince(ActivityStatsPeriod.ThisYear).Should().Be(stats.Downloads.ThisYear);
    }
}
