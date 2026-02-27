namespace MetaAnime.Core.Settings;

public record PlexSettings
{
    public const string SectionName = "Plex";
    public required string Token { get; init; }
    public required string Host { get; init; }
    public required string AnimeSections { get; init; }
}
