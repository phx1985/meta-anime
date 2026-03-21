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
    string QualityProfile
);
