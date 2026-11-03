using System.Text.RegularExpressions;

namespace MetaAnime.Application.Utilities;

/// <summary>A seasonal title being ranked. Tags mix MAL genres, themes and demographics.</summary>
public sealed record SeasonalCandidate(
    string MalId,
    string Title,
    IReadOnlyList<string> Tags,
    double Mean,
    string? ViewState,
    /// <summary>True when the title is already tracked in MAL, owned in Plex or in Sonarr.</summary>
    bool InLibrary = false);

/// <summary>One entry of the user's MAL list, as read from the local cache.</summary>
public sealed record TasteEntry(
    string MalId,
    string Title,
    int Score,
    string Status,
    IReadOnlyList<string> Tags);

public sealed record TasteReasonSource(string MalId, string Title);

/// <summary>Why a title was picked — always a real historical title, never a fabricated one.</summary>
public sealed record TasteReason(
    IReadOnlyList<string> Tags,
    IReadOnlyList<TasteReasonSource> Because,
    string Kind);

public sealed record ScoredSuggestion(
    SeasonalCandidate Candidate,
    double MatchScore,
    IReadOnlyList<TasteReason> Reasons);

public sealed record SuggestionOptions(
    int Limit = 16,
    int ProfileTopTags = 30,
    double MinTagWeight = 0.02,
    double MmrLambda = 0.7,
    int MaxPerFranchise = 2,
    int MaxPerDominantTag = 3)
{
    /// <summary>Pseudo-weights used when the user did not score a title (MAL score == 0).</summary>
    public static readonly IReadOnlyDictionary<string, double> StatusWeights = new Dictionary<string, double>(
        StringComparer.OrdinalIgnoreCase)
    {
        ["completed"] = 0.35,
        ["watching"] = 0.25,
        ["plan_to_watch"] = 0.05,
        ["on_hold"] = -0.10,
        ["dropped"] = -0.45,
    };

    /// <summary>Statuses that mean "already consumed" — those candidates are dropped.</summary>
    public static readonly IReadOnlySet<string> SeenStates = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "watching", "completed", "on_hold", "dropped",
    };

    /// <summary>Statuses that count as "the user saw the prequel".</summary>
    public static readonly IReadOnlySet<string> PrequelSatisfiedStates = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "watching", "completed",
    };
}

/// <summary>
/// Deterministic "seasonal suggestions" engine.
///
/// Scores a season against the user's own MAL scores/statuses with three components:
///   1. a <b>nearest-neighbour</b> similarity to one specific high-rated title, so the reason
///      always cites a real show that genuinely shares narrow tags;
///   2. a tag-specificity-weighted taste vector (generic tags such as "Action" count 0.35x);
///   3. the community mean as a light prior.
/// Then it drops what cannot honestly be suggested and de-duplicates with MMR.
/// Pure (no I/O) so it stays unit-testable.
/// </summary>
public static class SeasonalSuggestionCalculator
{
    private const double ScorePivot = 5.5;
    private const double ScoreScale = 4.5;

    /// <summary>Tags so broad they say nothing about taste — downweighted everywhere.</summary>
    private static readonly HashSet<string> GenericTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "Action", "Adventure", "Comedy", "Drama", "Fantasy", "Slice of Life", "Shounen", "Super Power",
    };

    /// <summary>Narrow tags: a single shared entry is enough to justify a "because you liked" reason.</summary>
    private static readonly HashSet<string> SpecificTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "Psychological", "Romance", "Horror", "Gore", "Cyberpunk", "Mystery", "Sci-Fi", "Supernatural",
        "Josei", "Isekai", "Mecha", "Thriller", "Seinen", "Shoujo", "Boys Love", "Girls Love", "Ecchi",
        "Harem", "Music", "Sports", "Martial Arts", "Time Travel", "Space", "Military", "Historical",
        "Vampire", "Demons", "Magic", "Parody", "Satire", "Survival", "Reincarnation", "Detective",
        "Adult Cast", "Gourmet", "Educational", "Idols (Female)", "Idols (Male)",
    };

    private static readonly Regex SequelMarker = new(
        @"(\bseason\s*(\d+|one|two|three|four|i{1,3}|iv)\b)|(\b\d+(st|nd|rd|th)\s+season\b)|(\bpart\s*(\d+|i{1,3}|iv)\b)|(\bcour\s*\d+\b)|(\bfinal\s+season\b)|(\s(i{1,3}|iv|v|vi{0,3}))\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex NonWord = new(@"[^\p{L}\p{N}]+", RegexOptions.Compiled);

    /// <summary>Trailing roman numeral left over once the marker words are gone ("… II").</summary>
    private static readonly Regex RomanSuffix = new(@"\s+(i{1,3}|iv|v|vi{0,3})$", RegexOptions.Compiled);

    /// <summary>0.35 for generic tags, 1.0 for narrow ones, 0.6 for everything in between.</summary>
    public static double TagWeight(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return 0d;
        if (SpecificTags.Contains(tag)) return 1.0;
        if (GenericTags.Contains(tag)) return 0.35;
        return 0.6;
    }

    public static IReadOnlyList<ScoredSuggestion> Compute(
        IEnumerable<SeasonalCandidate> candidates,
        IEnumerable<TasteEntry> history,
        SuggestionOptions? options = null)
    {
        options ??= new SuggestionOptions();

        var entries = history as IReadOnlyList<TasteEntry> ?? history.ToList();
        var profile = BuildProfile(entries, options);
        var neighbours = entries.Where(e => WeightOf(e) > 0).ToList();
        // No history → cold start: rank on community score alone, with "popular" reasons.
        var coldStart = profile.Count == 0 && neighbours.Count == 0;

        // ── What must never be suggested ────────────────────────────────────────
        var consumedMalIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var satisfiedFranchises = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            var state = entry.Status ?? string.Empty;
            if (SuggestionOptions.SeenStates.Contains(state))
                consumedMalIds.Add(entry.MalId);

            var key = FranchiseKey(entry.Title);
            if (key.Length > 0 && SuggestionOptions.PrequelSatisfiedStates.Contains(state))
                satisfiedFranchises.Add(key);
        }

        // ── Score ───────────────────────────────────────────────────────────────
        var scored = new List<ScoredSuggestion>();
        foreach (var candidate in candidates)
        {
            if (candidate.MalId.Length == 0) continue;

            // "For you" is strictly for NEW discovery: anything already tracked/owned is out.
            if (candidate.InLibrary) continue;

            var state = candidate.ViewState ?? string.Empty;
            if (SuggestionOptions.SeenStates.Contains(state)) continue;
            if (IsUnwatchedSequel(candidate.Title, satisfiedFranchises)) continue;

            // 1. Nearest neighbour over *specific* tags only — this is what the reason cites.
            var (neighbour, sharedTags, similarity) = FindNeighbour(candidate, neighbours);

            // 2. Tag-weighted taste vector.
            var taste = WeightedTaste(profile, candidate.Tags);

            // 3. Community mean as a light prior.
            var prior = Math.Clamp(candidate.Mean / 10.0, 0d, 1d);

            if (!coldStart && neighbour is null && taste <= 0.005) continue; // no honest overlap → grid only

            var score = 0.50 * similarity + 0.35 * Math.Max(0d, taste) + 0.15 * prior;

            // Already planned? Keep it, but behind fresh discoveries.
            if (string.Equals(state, "plan_to_watch", StringComparison.OrdinalIgnoreCase))
                score -= 0.05;

            var reasons = neighbour is not null
                ? new List<TasteReason>
                {
                    new(sharedTags, [new TasteReasonSource(neighbour.MalId, neighbour.Title)], "taste"),
                }
                : [new TasteReason([], [], "popular")];

            scored.Add(new ScoredSuggestion(candidate, score, reasons));
        }

        // ── Rank + diversify ────────────────────────────────────────────────────
        var ranked = scored
            .OrderByDescending(s => s.MatchScore)
            .ThenBy(s => s.Candidate.MalId, StringComparer.Ordinal)
            .Take(30)
            .ToList();

        return Diversify(ranked, options);
    }

    // ── Profile ─────────────────────────────────────────────────────────────────
    private static Dictionary<string, double> BuildProfile(IReadOnlyList<TasteEntry> entries, SuggestionOptions options)
    {
        var totals = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var denominator = 0d;

        foreach (var entry in entries)
        {
            var weight = WeightOf(entry);
            if (weight == 0d) continue;

            denominator += Math.Abs(weight);
            foreach (var tag in entry.Tags.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                totals[tag] = totals.GetValueOrDefault(tag) + weight;
            }
        }

        if (denominator <= 0d) return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        return totals
            .ToDictionary(kv => kv.Key, kv => kv.Value / (denominator + 1e-6), StringComparer.OrdinalIgnoreCase)
            .Where(kv => Math.Abs(kv.Value) >= options.MinTagWeight)
            .OrderByDescending(kv => Math.Abs(kv.Value))
            .Take(options.ProfileTopTags)
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>User score drives the weight; unscored titles fall back to their list status.</summary>
    public static double WeightOf(TasteEntry entry)
    {
        if (entry.Score > 0)
            return Math.Clamp((entry.Score - ScorePivot) / ScoreScale, -1d, 1d);

        return SuggestionOptions.StatusWeights.TryGetValue(entry.Status ?? string.Empty, out var w) ? w : 0d;
    }

    // ── Candidate scoring ───────────────────────────────────────────────────────
    /// <summary>Specificity-weighted average taste of the candidate's tags, roughly −1 … +1.</summary>
    private static double WeightedTaste(IReadOnlyDictionary<string, double> profile, IReadOnlyList<string> tags)
    {
        if (tags.Count == 0 || profile.Count == 0) return 0d;

        var sum = 0d;
        var weightSum = 0d;
        foreach (var tag in tags.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var w = TagWeight(tag);
            if (w <= 0) continue;
            weightSum += w;
            if (profile.TryGetValue(tag, out var taste)) sum += taste * w;
        }

        return weightSum <= 0d ? 0d : sum / weightSum;
    }

    /// <summary>
    /// Best neighbour among the user's high-rated titles, scored with a weighted Jaccard over
    /// non-generic tags. Returns null when no title shares enough to justify a citation.
    /// </summary>
    private static (TasteEntry? Entry, IReadOnlyList<string> SharedTags, double Similarity) FindNeighbour(
        SeasonalCandidate candidate,
        IReadOnlyList<TasteEntry> neighbours)
    {
        TasteEntry? best = null;
        var bestShared = new List<string>();
        var bestSim = 0d;

        var candidateTags = candidate.Tags
            .Where(t => TagWeight(t) >= 0.6)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (candidateTags.Count == 0) return (null, [], 0d);

        foreach (var entry in neighbours)
        {
            var entryTags = entry.Tags
                .Where(t => TagWeight(t) >= 0.6)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (entryTags.Count == 0) continue;

            var shared = candidateTags.Intersect(entryTags, StringComparer.OrdinalIgnoreCase).ToList();
            if (shared.Count == 0) continue;

            // One shared tag only counts when that tag is genuinely narrow.
            var hasSpecific = shared.Any(t => TagWeight(t) >= 1.0);
            if (shared.Count < 2 && !hasSpecific) continue;

            var union = candidateTags.Union(entryTags, StringComparer.OrdinalIgnoreCase).ToList();
            var unionWeight = union.Sum(TagWeight);
            if (unionWeight <= 0) continue;

            var sim = shared.Sum(TagWeight) / unionWeight;
            if (sim <= bestSim) continue;

            best = entry;
            bestShared = shared.OrderByDescending(TagWeight).ToList();
            bestSim = sim;
        }

        return (best, bestShared.Take(2).ToList(), bestSim);
    }

    // ── Franchise / sequel detection ────────────────────────────────────────────
    /// <summary>
    /// Franchise key = the title before the first ':' with punctuation collapsed.
    /// The Kometa tvdb/anidb ids are strictly per-MAL-entry (verified: 0 shared ids),
    /// so they cannot group seasons — the normalised title is the only available signal.
    /// </summary>
    public static string FranchiseKey(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return string.Empty;

        var primary = title.Split(':', 2)[0];
        primary = SequelMarker.Replace(primary, " ");   // "Jujutsu Kaisen 2nd Season" → "Jujutsu Kaisen"
        var key = NonWord.Replace(primary.Trim().ToLowerInvariant(), " ").Trim();
        key = RomanSuffix.Replace(key, string.Empty).Trim();
        return key;
    }

    /// <summary>True when the title advertises itself as a later season/part.</summary>
    public static bool HasSequelMarker(string? title) =>
        !string.IsNullOrWhiteSpace(title) && SequelMarker.IsMatch(title);

    private static bool IsUnwatchedSequel(string title, HashSet<string> satisfiedFranchises)
    {
        if (!HasSequelMarker(title)) return false;

        var key = FranchiseKey(title);
        if (key.Length == 0) return false;

        // Any later season/part is blocked unless the user completed or is watching that franchise.
        return !satisfiedFranchises.Contains(key);
    }

    // ── Diversity (MMR) ─────────────────────────────────────────────────────────
    private static List<ScoredSuggestion> Diversify(List<ScoredSuggestion> ranked, SuggestionOptions options)
    {
        var remaining = new List<ScoredSuggestion>(ranked);
        var selected = new List<ScoredSuggestion>();
        var franchiseCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var dominantTagCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        while (remaining.Count > 0 && selected.Count < options.Limit)
        {
            ScoredSuggestion? best = null;
            var bestValue = double.NegativeInfinity;

            foreach (var candidate in remaining)
            {
                var franchise = FranchiseKey(candidate.Candidate.Title);
                var dominant = DominantTag(candidate);

                if (franchiseCount.GetValueOrDefault(franchise) >= options.MaxPerFranchise) continue;
                if (dominant != null && dominantTagCount.GetValueOrDefault(dominant) >= options.MaxPerDominantTag) continue;

                var redundancy = selected.Count == 0
                    ? 0d
                    : selected.Max(s => Jaccard(s.Candidate.Tags, candidate.Candidate.Tags));

                var value = options.MmrLambda * candidate.MatchScore - (1 - options.MmrLambda) * redundancy;
                if (value > bestValue || (Math.Abs(value - bestValue) < 1e-9
                                          && string.CompareOrdinal(candidate.Candidate.MalId, best?.Candidate.MalId) < 0))
                {
                    best = candidate;
                    bestValue = value;
                }
            }

            if (best is null) break; // every remaining candidate is capped out

            selected.Add(best);
            remaining.Remove(best);
            var chosenKey = FranchiseKey(best.Candidate.Title);
            franchiseCount[chosenKey] = franchiseCount.GetValueOrDefault(chosenKey) + 1;
            var dom = DominantTag(best);
            if (dom != null) dominantTagCount[dom] = dominantTagCount.GetValueOrDefault(dom) + 1;
        }

        // Starvation floor: a one-genre taste profile can be fully capped out, which leaves the
        // 4-slot deck half empty. Top up from the remaining pool, ignoring the per-tag cap
        // (the franchise cap still applies).
        var floor = Math.Min(options.Limit, 4);
        while (selected.Count < floor && remaining.Count > 0)
        {
            ScoredSuggestion? next = null;
            var nextScore = double.NegativeInfinity;

            foreach (var candidate in remaining)
            {
                var key = FranchiseKey(candidate.Candidate.Title);
                if (franchiseCount.GetValueOrDefault(key) >= options.MaxPerFranchise) continue;

                if (candidate.MatchScore > nextScore)
                {
                    next = candidate;
                    nextScore = candidate.MatchScore;
                }
            }

            if (next is null) break;

            selected.Add(next);
            remaining.Remove(next);
            var nextKey = FranchiseKey(next.Candidate.Title);
            franchiseCount[nextKey] = franchiseCount.GetValueOrDefault(nextKey) + 1;
        }

        return selected
            .OrderByDescending(s => s.MatchScore)
            .ThenBy(s => s.Candidate.MalId, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// The candidate's most characteristic tag. Measured on the title itself (not on the cited
    /// reason) so the diversity cap also applies to cold-start picks that carry no reason tags.
    /// </summary>
    private static string? DominantTag(ScoredSuggestion suggestion) =>
        suggestion.Candidate.Tags
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .OrderByDescending(TagWeight)
            .ThenBy(t => t, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

    private static double Jaccard(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0d;

        var setA = new HashSet<string>(a, StringComparer.OrdinalIgnoreCase);
        var setB = new HashSet<string>(b, StringComparer.OrdinalIgnoreCase);
        var union = new HashSet<string>(setA, StringComparer.OrdinalIgnoreCase);
        union.UnionWith(setB);

        return union.Count == 0 ? 0d : (double)setA.Intersect(setB, StringComparer.OrdinalIgnoreCase).Count() / union.Count;
    }
}
