namespace MetaAnime.Application.Contracts;

public sealed record SeasonalAnimeResponse(
    string MalId,
    string Title,
    string? Synopsis,
    string[]? Genres,
    string? CoverImageUrl,
    double MalScore,
    string? StartDate,
    string? MediaType,
    string? Status,
    string? MalUrl,
    bool InMal,
    bool InPlex,
    string? PlexUrl,
    bool InSonarr,
    string? SonarrUrl
);
