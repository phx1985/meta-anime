namespace MetaAnime.Core.Settings;

public record KometaSettings
{
    public const string SectionName = "Kometa";
    public const string DefaultAnimeIdsJsonUrl = "https://raw.githubusercontent.com/Kometa-Team/Anime-IDs/refs/heads/master/anime_ids.json";
    public string? AnimeIdsJsonUrl { get; init; }
}
