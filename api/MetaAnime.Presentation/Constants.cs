namespace MetaAnime.Presentation;

public static class Constants
{
    public static class Messages
    {
        public const string CodecParameterRequired = "The 'codec' query parameter is required.";
        public const string SonarrCodecSearchError = "An error occurred while searching Sonarr for series with the requested codec.";
        public const string SonarrTermRequired = "The 'term' query parameter is required.";
        public const string SonarrTvdbIdRequired = "The 'tvdbId' must be a positive integer.";
        public const string SonarrRootFolderRequired = "The 'rootFolderPath' is required.";
        public const string MalAnimeIdRequired = "The 'animeId' path parameter is required.";
        public const string MalStatusInvalid = "The 'status' must be one of: watching, completed, on_hold, dropped, plan_to_watch.";
        public const string SeasonRequired = "The 'season' query parameter is required (winter, spring, summer, fall).";
        public const string SeasonInvalid = "The 'season' must be one of: winter, spring, summer, fall.";
        public const string YearInvalid = "The 'year' must be between 1917 and the current year + 1.";
    }
}
