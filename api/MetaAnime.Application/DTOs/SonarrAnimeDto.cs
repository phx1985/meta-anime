using MetaAnime.Core.Models;

namespace MetaAnime.Application.DTOs
{
    public sealed class SonarrAnimeDto(Anime? anime, string sonarrBaseUrl)
    {
        public string Title { get; } = anime?.Title ?? "NO_TITLE";
        public string? Id { get; } = anime?.SourceId;
        public string? Url { get; } = $"{sonarrBaseUrl}/series/{anime?.SourceId}";
    }
}
