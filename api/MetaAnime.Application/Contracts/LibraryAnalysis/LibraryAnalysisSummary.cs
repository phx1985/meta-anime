namespace MetaAnime.Application.Contracts.LibraryAnalysis;

/// <summary>
/// Top-level summary returned by the library analysis endpoint (Phase 1).
/// Contains aggregate statistics and a flat list of series rows — episode-file
/// detail fields are null until the client requests Phase 2 enrichment.
/// </summary>
public sealed record LibraryAnalysisSummary(
    long TotalSizeBytes,
    int TotalFileCount,
    int TotalSeriesCount,
    int MissingFileSeriesCount,
    IList<LibrarySeriesRow> Series
);
