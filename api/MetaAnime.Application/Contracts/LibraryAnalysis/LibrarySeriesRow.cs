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
    IList<LibraryEpisodeFileInfo>? EpisodeFiles
);
