namespace MetaAnime.Application.Contracts;

public sealed record ActivityStatsResponse(
    ActivityPeriodStats Downloads,
    StoragePeriodStats Storage,
    ActivityChartData Chart,
    string? LastUpdated = null);

public sealed record ActivityPeriodStats(
    int Today,
    int ThisWeek,
    int ThisMonth,
    int ThisYear);

public sealed record StoragePeriodStats(
    long Today,
    long ThisWeek,
    long ThisMonth,
    long ThisYear);

/// <summary>Single time-series point for the activity chart.</summary>
public sealed record ActivityChartPoint(
    string Label,
    int Downloads,
    long StorageBytes);

/// <summary>Pre-computed chart series for each selectable period.</summary>
public sealed record ActivityChartData(
    IList<ActivityChartPoint> Week,
    IList<ActivityChartPoint> Month,
    IList<ActivityChartPoint> Year);

/// <summary>One bucket of the multi-series activity trend.</summary>
public sealed record ActivityTrendPoint(
    string Label,
    int Ingested,
    int Added,
    int Watched,
    int Syncs);

/// <summary>Series shown together on the Activity hub chart (GET /api/v1/activity/trend).</summary>
public sealed record ActivityTrendResponse(
    string Period,
    IList<ActivityTrendPoint> Points);
