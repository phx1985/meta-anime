using MetaAnime.Core.Models;

namespace MetaAnime.Application.DTOs
{
    public sealed class PlexAnimeDto(Anime? anime, string plexBaseUrl)
    {
        public string Title { get; } = anime?.Title ?? "NO_TITLE";
        public string? Id { get; } = anime?.SourceId;
        public string? Url { get; } = $"{plexBaseUrl}{anime?.SourceId}";
    }
}
