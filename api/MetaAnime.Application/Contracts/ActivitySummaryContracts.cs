namespace MetaAnime.Application.Contracts;

/// <summary>
/// Net counters for the Activity hub KPI cards, over a rolling window (default: 30 days).
/// <para>
/// <c>NetAdded</c> is the whole point: a reverted add (an anime added then deleted) is
/// excluded, so "add one, delete it" reads 0 — not 1.
/// </para>
/// </summary>
public sealed record ActivitySummaryResponse(
    int Added,
    int Removed,
    int NetAdded,
    int Ingested,
    int Watched,
    int SeasonsCompleted,
    int SyncRuns,
    int Days);
