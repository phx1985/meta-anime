using System.Collections.Frozen;

namespace MetaAnime.Application;

/// <summary>
/// Shared MAL domain value constants used across Application services.
/// Centralises NSFW filters, airing statuses, and watch statuses to prevent duplication.
/// </summary>
public static class MalConstants
{
    /// <summary>Genre names always treated as NSFW regardless of the MAL <c>nsfw</c> field.</summary>
    public static readonly FrozenSet<string> NsfwGenres = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "Ecchi", "Hentai", "Erotica");

    /// <summary>MAL age-rating codes that indicate adult/NSFW content.</summary>
    public static readonly FrozenSet<string> NsfwRatings = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "r+", "rx");

    public static class AiringStatus
    {
        public const string FinishedAiring   = "finished_airing";
        public const string CurrentlyAiring  = "currently_airing";
        public const string NotYetAired      = "not_yet_aired";

        public static readonly FrozenSet<string> All = FrozenSet.Create(
            StringComparer.OrdinalIgnoreCase,
            FinishedAiring, CurrentlyAiring, NotYetAired);
    }

    public static class WatchStatus
    {
        public const string Watching     = "watching";
        public const string Completed    = "completed";
        public const string OnHold       = "on_hold";
        public const string Dropped      = "dropped";
        public const string PlanToWatch  = "plan_to_watch";
    }
}
