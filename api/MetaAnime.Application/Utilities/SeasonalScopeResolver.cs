namespace MetaAnime.Application.Utilities;

/// <summary>
/// Turns a scope ("current" | "last2" | "last4" | "custom") into the ordered list of
/// (year, season) windows to scan. Capped at 4 windows: that cap is what keeps the
/// number of MyAnimeList calls bounded for a single page view.
/// </summary>
public static class SeasonalScopeResolver
{
    /// <summary>Chronological season ordering used to walk a scope backwards.</summary>
    private static readonly string[] SeasonOrder = ["winter", "spring", "summer", "fall"];

    /// <summary>
    /// Resolves a scope into the ordered list of (year, season) windows to scan.
    /// Never returns more than 4 windows — that cap is what keeps MAL traffic bounded.
    /// </summary>
    public static IReadOnlyList<(int Year, string Season)> ResolveSeasons(
        int year, string season, string? scope,
        int? fromYear = null, string? fromSeason = null, int? toYear = null, string? toSeason = null)
    {
        static (int, string) Previous((int Year, string Season) w)
        {
            var idx = Array.IndexOf(SeasonOrder, w.Season);
            return idx <= 0 ? (w.Year - 1, SeasonOrder[^1]) : (w.Year, SeasonOrder[idx - 1]);
        }

        static int ToIndex(string s) => Math.Max(0, Array.IndexOf(SeasonOrder, s));

        var anchor = (Year: year, Season: season.ToLowerInvariant());

        switch ((scope ?? "current").ToLowerInvariant())
        {
            case "last2":
            case "last4":
            {
                var count = scope!.Equals("last4", StringComparison.OrdinalIgnoreCase) ? 4 : 2;
                var windows = new List<(int, string)> { anchor };
                var cursor = anchor;
                for (var i = 1; i < count; i++)
                {
                    cursor = Previous(cursor);
                    windows.Insert(0, cursor);
                }
                return windows;
            }
            case "custom":
            {
                if (fromYear is null || string.IsNullOrWhiteSpace(fromSeason) ||
                    toYear is null || string.IsNullOrWhiteSpace(toSeason))
                    return [anchor];

                var from = (Year: fromYear.Value, Season: fromSeason.ToLowerInvariant());
                var to = (Year: toYear.Value, Season: toSeason.ToLowerInvariant());
                if (!SeasonOrder.Contains(from.Season) || !SeasonOrder.Contains(to.Season))
                    return [anchor];

                // Tolerate a reversed range instead of returning nothing.
                if (from.Year > to.Year || (from.Year == to.Year && ToIndex(from.Season) > ToIndex(to.Season)))
                    (from, to) = (to, from);

                // Walk forward from `from` to `to`, bounded to 4 windows ending at `to`.
                var windows = new List<(int, string)>();
                var cursor = from;
                for (var guard = 0; guard < 60; guard++)
                {
                    windows.Add(cursor);
                    if (cursor.Year == to.Year && cursor.Season == to.Season) break;
                    var idx = ToIndex(cursor.Season);
                    cursor = idx == SeasonOrder.Length - 1 ? (cursor.Year + 1, SeasonOrder[0]) : (cursor.Year, SeasonOrder[idx + 1]);
                }

                return windows.Count > 4 ? windows.Skip(windows.Count - 4).ToList() : windows;
            }
            default:
                return [anchor];
        }
    }

}
