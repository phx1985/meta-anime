namespace MetaAnime.Application.Contracts.LibraryAnalysis;

/// <summary>
/// Individual episode file detail within a series, populated during Phase 2 enrichment.
/// </summary>
public sealed record LibraryEpisodeFileInfo(
    int EpisodeFileId,
    string RelativePath,
    long SizeBytes,
    string VideoCodec,
    string AudioCodec,
    string Resolution,
    int VideoBitDepth,
    double AudioChannels,
    string QualityProfile,
    /// <summary>Duration of the episode in seconds, parsed from Sonarr MediaInfo.RunTime. Falls back to 1380 (23 min) when unavailable.</summary>
    int DurationSeconds,
    /// <summary>True when RunTime was missing and the 23-min fallback was applied.</summary>
    bool IsDurationEstimated
);
