namespace MetaAnime.Infrastructure.Sonarr;

public static class Constants
{
    public static class Errors
    {
        public const string NotConfigured = "Sonarr.NotConfigured";
        public const string NotFound = "Sonarr.NotFound";
        /// <summary>The Sonarr history endpoint failed (unreachable, non-2xx, empty body).</summary>
        public const string DownloadHistory = "Sonarr.DownloadHistory";
    }
}
