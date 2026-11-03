using MetaAnime.Domain.Entities;

namespace MetaAnime.Core.Interfaces.Repositories;

/// <summary>
/// Read/write access to the local Sonarr download-history mirror
/// (see <see cref="SonarrHistoryEvent"/>).
/// </summary>
public interface ISonarrHistoryEventRepository
{
    /// <summary>Returns every mirrored event at or after <paramref name="fromUtc"/>, unsorted.</summary>
    Task<IReadOnlyList<SonarrHistoryEvent>> GetSinceAsync(DateTime fromUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Timestamp of the newest mirrored event, or <c>null</c> when the mirror is empty.
    /// Used as the incremental sync watermark.
    /// </summary>
    Task<DateTime?> GetLatestOccurredAtAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts the events that are not already stored (keyed by Sonarr history id) in a
    /// single bulk COPY and returns how many rows were added. Idempotent by design.
    /// </summary>
    Task<int> MergeAsync(IReadOnlyCollection<SonarrHistoryEvent> incoming, CancellationToken cancellationToken = default);

    /// <summary>Deletes every event older than <paramref name="utc"/> and returns the number of rows removed.</summary>
    Task<int> PruneBeforeAsync(DateTime utc, CancellationToken cancellationToken = default);
}
