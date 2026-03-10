namespace MetaAnime.Infrastructure.Plex;

public static class Constants
{
    /// <summary>Stable client identifier sent to plex.tv for OAuth pin flows.</summary>
    public const string PlexClientIdentifier = "meta-anime-settings-client";

    public static class Errors
    {
        public const string NotConfigured = "Plex.NotConfigured";
        public const string MachineIdentifierNotFound = "Plex.MachineIdentifierNotFound";
        public const string PlexPinFailed = "Plex.PinFailed";
        public const string ThumbNotFound = "Plex.ThumbNotFound";
    }
}
