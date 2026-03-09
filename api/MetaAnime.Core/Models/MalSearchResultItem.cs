namespace MetaAnime.Core.Models;

public sealed record MalSearchResultItem(
    string Id,
    string Title,
    string? ThumbnailUrl,
    int? Year,
    string? MediaType,
    bool IsAlreadyInLibrary
);
