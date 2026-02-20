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
            $"{plexBaseUrl}{anime?.SourceId}",
            null
        )
    { }

    public PlexAnimeInfo(PlexMedia? plexMedia, string plexBaseUrl, int viewedLeafCount, int leafCount)
        : this(
            plexMedia?.Title ?? "NO_TITLE",
            plexMedia?.Id,
            $"{plexBaseUrl}{plexMedia?.Id}",
            new PlexProgressionInfo(viewedLeafCount, leafCount)
        )
    { }
}

public sealed record PlexProgressionInfo(
    int ViewedEpisodes,
    int TotalEpisodes
);
