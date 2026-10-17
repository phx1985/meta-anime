namespace MetaAnime.Infrastructure.MyAnimeList;

public static class Constants
{
    public static class Errors
    {
        public const string MissingClientId = "MAL.MissingClientId";
        public const string MissingRefreshToken = "MAL.MissingRefreshToken";
        public const string RefreshError = "MAL.RefreshError";
        public const string RefreshFailed = "MAL.RefreshFailed";
        public const string InvalidResponse = "MAL.InvalidResponse";
        public const string NotConfigured = "MAL.NotConfigured";
        public const string NoToken = "MAL.NoToken";
        public const string AuthenticationFailed = "MAL.AuthenticationFailed";
        public const string InvalidSearchQuery = "MAL.InvalidSearchQuery";
        public const string SearchByTitleFailed = "MAL.SearchByTitleFailed";
        public const string SearchByTitleRichFailed = "MAL.SearchByTitleRichFailed";
    }
}
