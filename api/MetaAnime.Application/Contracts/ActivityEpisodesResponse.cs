namespace MetaAnime.Application.Contracts;

/// <summary>Selectable activity period — mirrors the four tabs of the activity card.</summary>
public enum ActivityStatsPeriod
{
    Today,
    ThisWeek,
    ThisMonth,
    ThisYear,
}

/// <summary>One imported episode, as listed by the "what was added" dialog.</summary>
/// <param name="SeriesTitle">Series title at import time (may be empty when Sonarr could not resolve it).</param>
/// <param name="SeasonNumber">Season number (0 = specials).</param>
/// <param name="EpisodeNumber">Episode number within the season.</param>
/// <param name="SizeBytes">Imported file size in bytes.</param>
/// <param name="OccurredAt">UTC import timestamp.</param>
public sealed record ActivityEpisodeItem(
    string SeriesTitle,
    int SeasonNumber,
    int EpisodeNumber,
    long SizeBytes,
    DateTime OccurredAt);

/// <summary>
/// Episode list for one activity period, newest first.
/// </summary>
/// <param name="Period">Period the list was built for.</param>
/// <param name="TotalCount">Total episodes imported in the period (may exceed <paramref name="Items"/>).</param>
/// <param name="TotalBytes">Total bytes imported in the period.</param>
/// <param name="Items">The most recent episodes, capped at the requested limit.</param>
public sealed record ActivityEpisodesResponse(
    ActivityStatsPeriod Period,
    int TotalCount,
    long TotalBytes,
    IList<ActivityEpisodeItem> Items);
