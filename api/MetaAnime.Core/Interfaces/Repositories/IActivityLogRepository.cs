using MetaAnime.Domain.Entities;

namespace MetaAnime.Core.Interfaces.Repositories;

public interface IActivityLogRepository
{
    Task AddAsync(ActivityLog entry, CancellationToken cancellationToken = default);

    /// <summary>Newest first, optionally restricted to one type.</summary>
    Task<IReadOnlyList<ActivityLog>> GetRecentAsync(
        IReadOnlyCollection<ActivityType>? types, int skip, int take, string? search = null,
        DateTimeOffset? from = null, CancellationToken cancellationToken = default);

    Task<int> CountAsync(
        IReadOnlyCollection<ActivityType>? types, DateTimeOffset? from = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks untouched <c>AnimeAdded</c> rows for this anime as reverted and returns how many
    /// were flipped. Used when Sonarr reports the series deleted (or a sync diff loses it).
    /// </summary>
    Task<int> RevertAddedAsync(int? malId, int? tvdbId, CancellationToken cancellationToken = default);

    /// <summary>Counts per type inside a time window, plus the net (un-reverted) add count.</summary>
    Task<Dictionary<ActivityType, int>> CountByTypeAsync(
        DateTimeOffset from, CancellationToken cancellationToken = default);

    /// <summary>
    /// Newest row of <paramref name="type"/> inside the window that refers to the same anime,
    /// matched by MAL id, TVDB id or normalized title. Used to fold a second provider's add into
    /// the row the first provider created instead of rendering two cards.
    /// </summary>
    Task<ActivityLog?> FindRecentCandidateAsync(
        ActivityType type, int? malId, int? tvdbId, string title, TimeSpan window, CancellationToken cancellationToken = default);

    /// <summary>True when an ingested provider event was already recorded (poll idempotency).</summary>
    Task<bool> ExistsByExternalEventKeyAsync(string externalEventKey, CancellationToken cancellationToken = default);

    /// <summary>Persists in-place changes to an existing row (provider consolidation).</summary>
    Task UpdateAsync(ActivityLog entry, CancellationToken cancellationToken = default);

    /// <summary>Every event since <paramref name="from"/> — feeds the multi-series trend chart.</summary>
    Task<IReadOnlyList<ActivityLog>> GetSinceAsync(DateTimeOffset from, CancellationToken cancellationToken = default);
}
