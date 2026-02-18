namespace MetaAnime.Core.Settings
{
    public record AuthenticationSettings
    {
        public const string SectionName = "Authentication";
        public required string ApiKey { get; init; }
    }
}
