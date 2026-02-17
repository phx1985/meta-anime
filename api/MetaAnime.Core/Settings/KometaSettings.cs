namespace MetaAnime.Core.Settings
{
    public record KometaSettings
    {
        public const string SectionName = "Kometa";
        public required string AnimeIdsJsonUrl { get; init; }
    }
}
