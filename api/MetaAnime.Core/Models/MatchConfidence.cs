namespace MetaAnime.Core.Models;

/// <summary>
/// Confidence tier assigned to a cross-source match produced by the matching engine.
/// </summary>
public enum MatchConfidence
{
    /// <summary>Matched through exact platform IDs (Kometa / external identifiers).</summary>
    ExactId,

    /// <summary>Fuzzy title match corroborated by year (and tie-broken by episode counts).</summary>
    StrongTitleAndYear,

    /// <summary>Title-only match without year corroboration — never auto-persisted.</summary>
    TitleOnly,
}
