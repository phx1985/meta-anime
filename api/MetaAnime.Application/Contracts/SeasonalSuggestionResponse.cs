namespace MetaAnime.Application.Contracts;

/// <summary>A title (from the user's own list) that explains why a suggestion was picked.</summary>
public sealed record SuggestionReasonSource(string MalId, string Title);

/// <summary>Explainability payload — never empty: falls back to <c>kind = "popular"</c>.</summary>
public sealed record SuggestionReason(string[] Tags, SuggestionReasonSource[] Because, string Kind);

/// <summary>One "For you" card: the seasonal DTO plus its match score and reason.</summary>
public sealed record SeasonalSuggestionResponse(
    string MalId,
    string Title,
    string? EnglishTitle,
    string? CoverImageUrl,
    string? Synopsis,
    string[]? Genres,
    double MalScore,
    string? StartDate,
    string? MediaType,
    string? Status,
    string? MalUrl,
    bool InMal,
    bool InPlex,
    string? PlexUrl,
    bool InSonarr,
    string? SonarrUrl,
    string? TvdbId,
    string? AniDbId,
    string? AgeRating,
    string? MalViewState,
    double MatchScore,
    SuggestionReason[] Reasons,
    string? EndDate = null,
    int? NumEpisodes = null,
    int? AiredEpisodesEst = null,
    /// <summary>True when AniList reports a Chinese/Taiwanese country of origin.</summary>
    bool IsDonghua = false);
