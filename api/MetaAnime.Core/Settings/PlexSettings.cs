namespace MetaAnime.Core.Settings
{
    public record PlexSettings
    {
        public const string SectionName = "Plex";
        public const string DefaultClientIdentifier = "meta-anime";
        public required string Token { get; init; }
        public required string Host { get; init; }
        public required string AnimeSections { get; init; }
        /// <summary>
        /// Unique identifier for this Meta-Anime client instance sent to plex.tv as X-Plex-Client-Identifier.
        /// If not set, a stable default value is used.
        /// </summary>
        public string ClientIdentifier { get; init; } = DefaultClientIdentifier;
    }
}
