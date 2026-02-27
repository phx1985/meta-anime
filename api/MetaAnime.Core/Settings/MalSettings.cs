namespace MetaAnime.Core.Settings;

public record MalSettings
{
    public const string SectionName = "MyAnimeList";
    public required string ApiBaseUrl { get; init; }
    public required string ApiToken { get; init; }
    public required string WebBaseUrl { get; init; }
    public string? ClientId { get; init; }
    public string? ClientSecret { get; init; }
    public string? RefreshToken { get; init; }
}
