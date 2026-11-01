namespace MetaAnime.Core.Models;

/// <summary>
/// A candidate counterpart considered for an unmatched anime row, reported so a future
/// ambiguity queue (user confirmation flow) can surface it without re-running the match.
/// </summary>
public sealed record MatchCandidateHint(
    string AnimeSource,
    string SourceId,
    string Title,
    MatchConfidence Confidence,
    double Similarity);

/// <summary>
/// An anime row that could not be matched unambiguously, together with its best candidates.
/// </summary>
public sealed record UnmatchedCandidate(
    string AnimeSource,
    string SourceId,
    string Title,
    IReadOnlyList<MatchCandidateHint> Candidates);

/// <summary>
/// Structured outcome of a matching run: per-tier mapping counts plus the unmatched
/// candidates kept for a future ambiguity queue.
/// </summary>
public sealed record MatchReport(
    int ExactIdMappings,
    int StrongTitleYearMappings,
    int TitleOnlyCandidates,
    IReadOnlyList<UnmatchedCandidate> UnmatchedCandidates);
