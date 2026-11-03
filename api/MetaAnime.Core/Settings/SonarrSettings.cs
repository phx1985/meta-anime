namespace MetaAnime.Core.Settings;

public record SonarrSettings
{
    public const string SectionName = "Sonarr";
    public required string Host { get; init; }
    public required string ApiKey { get; init; }
    public required string AnimeTags { get; init; }
    /// <summary>
    /// Optional remote URI for Sonarr used as the base URL for series links in the frontend.
    /// When set, this URL is used instead of Host for building series URLs.
    /// </summary>
    public string? RemoteUrl { get; init; }
    /// <summary>
    /// Default for the per-add "search for missing episodes" flag. Individual add requests can
    /// override it; null there falls back to this value.
    /// </summary>
    public bool SearchForMissingEpisodes { get; init; }
}
