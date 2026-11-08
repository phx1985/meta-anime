using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MetaAnime.Infrastructure.Repositories;

public sealed class ActivityLogRepository(MetaAnimeDbContext context) : IActivityLogRepository
{
    public async Task AddAsync(ActivityLog entry, CancellationToken cancellationToken = default)
    {
        context.ActivityLogs.Add(entry);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ActivityLog>> GetRecentAsync(
        IReadOnlyCollection<ActivityType>? types, int skip, int take, string? search = null,
        DateTimeOffset? from = null, CancellationToken cancellationToken = default)
    {
        var query = context.ActivityLogs.AsNoTracking();
        if (from is not null)
            query = query.Where(a => a.Timestamp >= from);
        if (types is { Count: > 0 } && types.Count < Enum.GetValues<ActivityType>().Length)
            query = query.Where(a => types.Contains(a.Type));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(a =>
                a.Title.ToLower().Contains(term) ||
                (a.Subtitle != null && a.Subtitle.ToLower().Contains(term)));
        }

        return await query
            .OrderByDescending(a => a.Timestamp)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> RevertAddedAsync(int? malId, int? tvdbId, CancellationToken cancellationToken = default)
    {
        if (malId is null && tvdbId is null) return 0;

        var query = context.ActivityLogs.Where(a =>
            a.Type == ActivityType.AnimeAdded && !a.IsReverted &&
            ((malId != null && a.MalId == malId) || (tvdbId != null && a.TvdbId == tvdbId)));

        var rows = await query.ToListAsync(cancellationToken);
        foreach (var row in rows) row.IsReverted = true;

        if (rows.Count > 0) await context.SaveChangesAsync(cancellationToken);
        return rows.Count;
    }

    public async Task<Dictionary<ActivityType, int>> CountByTypeAsync(
        DateTimeOffset from, CancellationToken cancellationToken = default)
    {
        var rows = await context.ActivityLogs
            .AsNoTracking()
            .Where(a => a.Timestamp >= from)
            .Select(a => new { a.Type, a.IsReverted })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => r.Type)
            .ToDictionary(
                g => g.Key,
                g => g.Key == ActivityType.AnimeAdded
                    ? g.Count(r => !r.IsReverted)
                    : g.Count());
    }

    public Task<int> CountAsync(
        IReadOnlyCollection<ActivityType>? types, DateTimeOffset? from = null, CancellationToken cancellationToken = default)
    {
        var query = context.ActivityLogs.AsNoTracking();
        if (from is not null) query = query.Where(a => a.Timestamp >= from);

        return types is null || types.Count == 0 || types.Count >= Enum.GetValues<ActivityType>().Length
            ? query.CountAsync(cancellationToken)
            : query.CountAsync(a => types.Contains(a.Type), cancellationToken);
    }

    public Task<ActivityLog?> FindRecentCandidateAsync(
        ActivityType type, int? malId, int? tvdbId, string title, TimeSpan window, CancellationToken cancellationToken = default)
    {
        var since = DateTimeOffset.UtcNow - window;
        var normalized = (title ?? string.Empty).Trim().ToLowerInvariant();

        // Ids are the reliable key; the title is the fallback for a provider that only knows one
        // of the two ids (MAL creates a row with malId, Sonarr with tvdbId + malId).
        return context.ActivityLogs
            .Where(a => a.Type == type && !a.IsReverted && a.Timestamp >= since)
            .Where(a =>
                (malId != null && a.MalId == malId) ||
                (tvdbId != null && a.TvdbId == tvdbId) ||
                (normalized != string.Empty && a.Title.ToLower() == normalized))
            .OrderByDescending(a => a.Timestamp)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<bool> ExistsByExternalEventKeyAsync(string externalEventKey, CancellationToken cancellationToken = default) =>
        context.ActivityLogs.AnyAsync(a => a.ExternalEventKey == externalEventKey, cancellationToken);

    public async Task UpdateAsync(ActivityLog entry, CancellationToken cancellationToken = default)
    {
        context.ActivityLogs.Update(entry);
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ActivityLog>> GetSinceAsync(
        DateTimeOffset from, CancellationToken cancellationToken = default)
        => await context.ActivityLogs
            .AsNoTracking()
            .Where(a => a.Timestamp >= from)
            .OrderBy(a => a.Timestamp)
            .ToListAsync(cancellationToken);
}
