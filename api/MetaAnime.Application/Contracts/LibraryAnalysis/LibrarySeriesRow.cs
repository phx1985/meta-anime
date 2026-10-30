namespace MetaAnime.Application.Contracts.LibraryAnalysis;

/// <summary>
/// Per-series row in the library analysis grid.
/// Phase 1 populates the size/count fields; Phase 2 enriches codec distributions and episode files.
/// </summary>
public sealed record LibrarySeriesRow(
    int SonarrId,
    string Title,
    string Path,
    bool Ended,
    int FileCount,
    int TotalEpisodes,
    long TotalSizeBytes,
    long AvgEpisodeSizeBytes,
    double PercentDownloaded,
    string? PrimaryVideoCodec,
    string? PrimaryAudioCodec,
    string? PrimaryResolution,
    int? PrimaryBitDepth,
    Dictionary<string, int>? VideoCodecDistribution,
    Dictionary<string, int>? AudioCodecDistribution,
    Dictionary<string, int>? ResolutionDistribution,
    IList<LibraryEpisodeFileInfo>? EpisodeFiles,
    /// <summary>MetaAnime local anime ID (Guid string) if this Sonarr series is matched in the local library; null otherwise.</summary>
    string? LocalAnimeId,
    /// <summary>Total duration of all episode files in seconds. 0 when duration data is unavailable (Phase 1 or no episode files).</summary>
    int TotalDurationSeconds,
    /// <summary>Average storage rate in GiB per hour (TotalSizeBytes / 1 GiB / (TotalDurationSeconds / 3600)). 0 when duration is unavailable.</summary>
    double GibPerHour,
    /// <summary>Sonarr next-airing date (null when ended or between seasons). Drives the season-aware status filter.</summary>
    DateTime? NextAiringDate = null
);
