using MetaAnime.Domain.Entities;
using MetaAnime.Infrastructure.Plex.Models;

namespace MetaAnime.Application.Contracts;

public sealed record PlexAnimeInfo(
    string Title,
    string? Id,
    string? Url,
    PlexProgressionInfo? Progression
)
{
    public PlexAnimeInfo(Anime? anime, string plexBaseUrl)
        : this(
            anime?.Title ?? "NO_TITLE",
            anime?.SourceId,
            BuildUrl(plexBaseUrl, anime?.SourceId),
            null
        )
    { }

    public PlexAnimeInfo(PlexMedia? plexMedia, string plexBaseUrl, int viewedLeafCount, int leafCount)
        : this(
            plexMedia?.Title ?? "NO_TITLE",
            plexMedia?.Id,
            BuildUrl(plexBaseUrl, plexMedia?.Id),
            new PlexProgressionInfo(viewedLeafCount, leafCount)
        )
    { }

    /// <summary>
    /// Builds the Plex Web deep link, or <c>null</c> when it cannot be formed.
    /// Returns null (rather than a truncated URL) when either the base URL (derived
    /// from the server machine identifier) or the item's rating key is missing, so
    /// the UI can treat "no link" as "not openable" instead of opening garbage.
    /// </summary>
    private static string? BuildUrl(string? baseUrl, string? ratingKey)
        => string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(ratingKey)
            ? null
            : $"{baseUrl}{ratingKey}";
}

public sealed record PlexProgressionInfo(
    int ViewedEpisodes,
    int TotalEpisodes
);
