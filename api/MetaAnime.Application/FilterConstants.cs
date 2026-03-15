namespace MetaAnime.Application;

/// <summary>
/// String constants for filter, sort, and source values used in <c>AnimeFilters</c> and related query objects.
/// </summary>
public static class FilterConstants
{
    public static class Source
    {
        public const string Plex   = "plex";
        public const string Mal    = "mal";
        public const string Sonarr = "sonarr";
    }

    public static class SortField
    {
        public const string Title   = "title";
        public const string AniDbId = "anidbid";
        public const string MalScore = "malscore";
        public const string MyScore  = "myscore";
    }

    public static class SortOrder
    {
        public const string Descending = "desc";
        public const string Ascending  = "asc";
    }

    public static class Operator
    {
        public const string And = "and";
        public const string Or  = "or";
    }
}
