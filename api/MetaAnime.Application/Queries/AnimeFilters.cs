namespace MetaAnime.Application.Queries;

public record AnimeFilters(
    string? Title = null,
    string? Source = null,
    string? Status = null,
    int? Year = null,
    string? SortBy = null,
    string? SortOrder = null
);
