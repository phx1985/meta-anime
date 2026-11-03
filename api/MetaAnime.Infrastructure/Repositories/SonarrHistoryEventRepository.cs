using LinqToDB.EntityFrameworkCore;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MetaAnime.Infrastructure.Repositories;

public sealed class SonarrHistoryEventRepository(MetaAnimeDbContext dbContext) : ISonarrHistoryEventRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<SonarrHistoryEvent>> GetSinceAsync(DateTime fromUtc, CancellationToken cancellationToken = default)
    {
        return await dbContext.SonarrHistoryEvents
            .AsNoTracking()
            .Where(e => e.OccurredAt >= fromUtc)
            .OrderByDescending(e => e.OccurredAt)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<DateTime?> GetLatestOccurredAtAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.SonarrHistoryEvents
            .AsNoTracking()
            .OrderByDescending(e => e.OccurredAt)
            .Select(e => (DateTime?)e.OccurredAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> MergeAsync(IReadOnlyCollection<SonarrHistoryEvent> incoming, CancellationToken cancellationToken = default)
    {
        if (incoming.Count == 0)
            return 0;

        // Ids come from Sonarr's own (strictly incrementing) history table, so a single
        // range lookup collects every id we may already hold — no giant IN list.
        var minId = incoming.Min(e => e.Id);
        var maxId = incoming.Max(e => e.Id);
        var knownIds = (await dbContext.SonarrHistoryEvents
                .AsNoTracking()
                .Where(e => e.Id >= minId && e.Id <= maxId)
                .Select(e => e.Id)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var toInsert = new List<SonarrHistoryEvent>(incoming.Count);
        foreach (var evt in incoming)
        {
            // HashSet.Add also de-duplicates repeats inside the incoming batch itself.
            if (knownIds.Add(evt.Id))
                toInsert.Add(evt);
        }

        if (toInsert.Count == 0)
            return 0;

        // One COPY for the whole batch (bypasses the EF command log; no per-row statements).
        await dbContext.BulkCopyAsync(toInsert, cancellationToken: cancellationToken);
        return toInsert.Count;
    }

    /// <inheritdoc />
    public async Task<int> PruneBeforeAsync(DateTime utc, CancellationToken cancellationToken = default)
    {
        return await dbContext.SonarrHistoryEvents
            .Where(e => e.OccurredAt < utc)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
