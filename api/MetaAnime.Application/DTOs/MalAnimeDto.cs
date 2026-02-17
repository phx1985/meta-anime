using MetaAnime.Core.Models;

namespace MetaAnime.Application.DTOs
{
    public sealed class MalAnimeDto(Anime? anime, string malBaseUrl)
    {
        public string Title { get; } = anime?.Title ?? "NO_TITLE";
        public string? Id { get; } = anime?.SourceId;
        public string? Url { get; } = $"{malBaseUrl}/anime/{anime?.SourceId}";
    }
}
