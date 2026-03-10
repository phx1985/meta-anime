namespace MetaAnime.Application.Contracts;

public sealed record CoverageGapItemResponse(
    string AniDbId,
    string Title,
    string? MalUrl,
    string? PlexUrl,
    string? SonarrUrl,
    PlexProgressionInfo? PlexProgress,
    int? Year,
    string? OriginalTitle,
    string? ThumbnailUrl
);
