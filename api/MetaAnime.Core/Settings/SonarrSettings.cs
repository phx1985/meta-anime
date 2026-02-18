namespace MetaAnime.Core.Settings
{
    public record SonarrSettings
    {
        public const string SectionName = "Sonarr";
        public required string Host { get; init; }
        public required string ApiKey { get; init; }
        public required string AnimeTags { get; init; }
    }
}
