namespace MetaAnime.Application.Contracts;

/// <summary>
/// Outcome of one incremental Sonarr download-history sync.
/// </summary>
/// <param name="Fetched">Events returned by Sonarr for the requested window (includes re-fetched overlap).</param>
/// <param name="Inserted">Events that were not already mirrored.</param>
/// <param name="SinceUtc">Window start actually requested from Sonarr.</param>
/// <param name="ElapsedMs">Wall-clock duration of the whole sync.</param>
public sealed record DownloadHistorySyncSummary(
    int Fetched,
    int Inserted,
    DateTime SinceUtc,
    long ElapsedMs);
