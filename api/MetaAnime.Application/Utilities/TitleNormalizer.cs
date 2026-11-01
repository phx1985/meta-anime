using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MetaAnime.Application.Utilities;

/// <summary>
/// Multi-signal title normalization used before fuzzy matching: Unicode NFKC, case folding,
/// punctuation stripping, article removal and roman-numeral → arabic translation.
/// Two titles that describe the same show normalize to the same string whenever possible
/// ("Fate/stay night: Unlimited Blade Works" == "Fate Stay Night Unlimited Blade Works").
/// Season markers ("Season 2", "2nd season", trailing "II") are extracted separately so
/// franchise seasons can be compared on their base titles and disambiguated explicitly.
/// </summary>
public static partial class TitleNormalizer
{
    private static readonly HashSet<string> Articles = new(StringComparer.Ordinal) { "a", "an", "the" };

    [GeneratedRegex(@"[^\p{L}\p{Nd}\s]")]
    private static partial Regex PunctuationRegex();

    /// <summary>
    /// Roman numerals of two or more characters only. Single-character numerals (i, v, x) are
    /// deliberately excluded: they collide with real words ("I", "mix", "V for Vendetta").
    /// </summary>
    [GeneratedRegex(@"^[ivx]+$")]
    private static partial Regex RomanTokenRegex();

    [GeneratedRegex(@"^x{0,3}(ix|iv|v?i{0,3})$")]
    private static partial Regex RomanPatternRegex();

    // "season 2", "part 2", "series 2" …
    [GeneratedRegex(@"\b(?:season|part|series|stage|act)\s+(\d{1,2})\b")]
    private static partial Regex MarkerAfterKeywordRegex();

    // "2nd season", "2 season", "2 part" …
    [GeneratedRegex(@"\b(\d{1,2})(?:st|nd|rd|th)?\s+(?:season|part)\b")]
    private static partial Regex MarkerBeforeKeywordRegex();

    /// <summary>
    /// Returns the normalized form of a title: NFKC → case fold → punctuation → spaces →
    /// roman numerals translated to arabic → articles removed → whitespace collapsed.
    /// Returns <see cref="string.Empty"/> for null/blank input.
    /// </summary>
    public static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var folded = raw.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        var tokens = PunctuationRegex().Replace(folded, " ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var sb = new StringBuilder(raw.Length);
        foreach (var token in tokens)
        {
            var word = TryTranslateRoman(token);
            if (Articles.Contains(word))
                continue;
            if (sb.Length > 0)
                sb.Append(' ');
            sb.Append(word);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Normalizes a title and strips its franchise season marker ("season 2" / "2nd season" /
    /// trailing "2" / trailing roman numeral). Returns the season-stripped base title plus the
    /// marker. Season 1 and no marker are indistinguishable and both report <c>null</c>.
    /// </summary>
    public static (string Base, int? Season) NormalizeWithSeason(string? raw)
    {
        var normalized = Normalize(raw);
        if (normalized.Length == 0)
            return (string.Empty, null);

        int? season = null;
        var baseTitle = normalized;

        var keywordMatch = MarkerAfterKeywordRegex().Match(normalized);
        if (keywordMatch.Success
            && int.TryParse(keywordMatch.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var keywordSeason))
        {
            season = keywordSeason;
            baseTitle = MarkerAfterKeywordRegex().Replace(baseTitle, " ");
        }
        else
        {
            var beforeMatch = MarkerBeforeKeywordRegex().Match(normalized);
            if (beforeMatch.Success
                && int.TryParse(beforeMatch.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var beforeSeason))
            {
                season = beforeSeason;
                baseTitle = MarkerBeforeKeywordRegex().Replace(baseTitle, " ");
            }
            else
            {
                // Trailing bare digit (e.g. "shingeki no kyojin 3") — only a single digit so
                // that titles ending in years or numbers ("86") are not mistaken for sequels.
                var tokens = baseTitle.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length >= 2
                    && tokens[^1].Length == 1
                    && char.IsAsciiDigit(tokens[^1][0])
                    && int.TryParse(tokens[^1], NumberStyles.None, CultureInfo.InvariantCulture, out var trailing)
                    && trailing is >= 1 and <= 9)
                {
                    season = trailing;
                    baseTitle = string.Join(' ', tokens.Take(tokens.Length - 1));
                }
            }
        }

        baseTitle = string.Join(' ', baseTitle.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return (baseTitle, season == 1 ? null : season);
    }

    /// <summary>
    /// Extracts the franchise season marker of a title: "season 2" / "2nd season" /
    /// "part ii" → 2, a trailing "… 2" → 2. Season 1 and no marker both report <c>null</c>.
    /// </summary>
    public static int? ExtractSeasonMarker(string? raw)
        => NormalizeWithSeason(raw).Season;

    private static string TryTranslateRoman(string token)
    {
        // Two characters or more only: single-letter numerals are too ambiguous in titles.
        if (token.Length < 2 || !RomanTokenRegex().IsMatch(token))
            return token;
        if (!RomanPatternRegex().IsMatch(token))
            return token;

        var value = 0;
        for (var i = 0; i < token.Length; i++)
        {
            var current = RomanValue(token[i]);
            var next = i + 1 < token.Length ? RomanValue(token[i + 1]) : 0;
            value += current < next ? -current : current;
        }

        return value > 0 ? value.ToString(CultureInfo.InvariantCulture) : token;
    }

    private static int RomanValue(char c) => c switch
    {
        'i' => 1,
        'v' => 5,
        'x' => 10,
        _ => 0,
    };
}
