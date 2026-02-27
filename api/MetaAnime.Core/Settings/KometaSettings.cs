namespace MetaAnime.Core.Settings;

public record KometaSettings
{
    public const string SectionName = "Kometa";
    public string? AnimeIdsJsonUrl { get; init; }
}
