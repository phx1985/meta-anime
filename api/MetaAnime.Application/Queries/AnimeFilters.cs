namespace MetaAnime.Application.Queries;

public record AnimeFilters(
    string? Title = null,
    string? Source = null,
    string[]? Status = null,
    string[]? Genre = null,
    string? GenreOperator = "or",
    string? SortBy = null,
    string? SortOrder = null,
    string[]? AiringStatus = null,
    bool? HasFile = null,
    string[]? Codec = null
);
