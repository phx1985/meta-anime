namespace MetaAnime.Application.Utilities;

/// <summary>
/// Word-level fuzzy similarity used by both the mediator search and the matching engine.
/// Extracted from <c>AnimeMediatorService</c> so both call sites share one implementation
/// (and its length-ratio short-circuit).
/// </summary>
public static class TitleSimilarity
{
    /// <summary>
    /// Skip fuzzy comparison entirely when the length ratio can never reach the caller's
    /// similarity threshold (the search/matching thresholds are ≥ 0.8, so anything below
    /// 0.2 length ratio is guaranteed to fail).
    /// </summary>
    private const double MinLengthRatioForFuzzy = 0.2;

    /// <summary>
    /// Computes a word-level fuzzy similarity between a search query and an anime title.
    /// For each word in the query, finds the best-matching word in the title using normalised
    /// Levenshtein distance, then averages those per-word scores.
    /// This correctly handles single-character typos (e.g. "dargon ball" ≈ "Dragon Ball Z").
    /// Returns a value in [0.0, 1.0].
    /// </summary>
    public static double ComputeWordLevelFuzzySimilarity(string title, string query)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(query)) return 0.0;

        var qWords = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var tWords = title.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (qWords.Length == 0 || tWords.Length == 0) return 0.0;

        double totalScore = 0;
        foreach (var qWord in qWords)
        {
            double bestWordScore = 0;
            foreach (var tWord in tWords)
            {
                double score = NormalisedLevenshteinSimilarity(qWord, tWord);
                if (score > bestWordScore) bestWordScore = score;
                if (bestWordScore >= 1.0) break; // exact match found – no need to check further
            }
            totalScore += bestWordScore;
        }

        return totalScore / qWords.Length;
    }

    /// <summary>
    /// Returns 1 − (Levenshtein(a, b) / max(|a|, |b|)), i.e. 1.0 for identical strings.
    /// </summary>
    public static double NormalisedLevenshteinSimilarity(string a, string b)
    {
        if (a == b) return 1.0;
        int maxLen = Math.Max(a.Length, b.Length);
        if (maxLen == 0) return 1.0;
        // Skip if lengths differ too much to ever reach the caller's threshold
        int minLen = Math.Min(a.Length, b.Length);
        if ((double)minLen / maxLen < MinLengthRatioForFuzzy) return 0.0;
        return 1.0 - (double)LevenshteinDistance(a, b) / maxLen;
    }

    /// <summary>Standard iterative Levenshtein (edit) distance.</summary>
    public static int LevenshteinDistance(string s, string t)
    {
        int m = s.Length, n = t.Length;
        if (m == 0) return n;
        if (n == 0) return m;

        var prev = new int[n + 1];
        var curr = new int[n + 1];

        for (int j = 0; j <= n; j++) prev[j] = j;

        for (int i = 1; i <= m; i++)
        {
            curr[0] = i;
            for (int j = 1; j <= n; j++)
            {
                int cost = s[i - 1] == t[j - 1] ? 0 : 1;
                curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            Array.Copy(curr, prev, n + 1);
        }

        return prev[n];
    }
}
