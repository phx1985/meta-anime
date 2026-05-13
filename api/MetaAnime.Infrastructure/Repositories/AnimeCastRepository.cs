using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MetaAnime.Infrastructure.Repositories;

public sealed class AnimeCastRepository(MetaAnimeDbContext dbContext) : IAnimeCastRepository
{
    public async Task<IEnumerable<AnimeCast>> GetByAnimeMalIdAsync(string animeMalId)
        => await dbContext.AnimeCast
            .AsNoTracking()
            .Where(ac => ac.AnimeMalId == animeMalId)
            .ToListAsync();

    public async Task<IEnumerable<AnimeCast>> GetByVaPersonIdAsync(int vaPersonId)
        => await dbContext.AnimeCast
            .AsNoTracking()
            .Where(ac => ac.VaPersonId == vaPersonId)
            .ToListAsync();

    public async Task BulkUpsertAsync(IEnumerable<AnimeCast> entries)
    {
        // Deduplicate by the unique constraint key in case caller didn't
        var list = entries
            .GroupBy(e => (e.AnimeMalId, e.CharacterId, e.Language))
            .Select(g => g.First())
            .ToList();

        if (list.Count == 0) return;

        var malId = list[0].AnimeMalId;

        // Batch-load all existing rows for this anime in one query
        var existing = await dbContext.AnimeCast
            .Where(ac => ac.AnimeMalId == malId)
            .ToDictionaryAsync(ac => (ac.CharacterId, ac.Language));

        foreach (var entry in list)
        {
            var key = (entry.CharacterId, entry.Language);
            if (existing.TryGetValue(key, out var existingEntry))
            {
                existingEntry.CharacterName    = entry.CharacterName;
                existingEntry.CharacterRole    = entry.CharacterRole;
                existingEntry.CharacterImageUrl = entry.CharacterImageUrl;
                existingEntry.VaPersonId       = entry.VaPersonId;
                existingEntry.VaName           = entry.VaName;
                existingEntry.VaImageUrl       = entry.VaImageUrl;
                existingEntry.SyncedAt         = entry.SyncedAt;
            }
            else
            {
                entry.Id = Guid.CreateVersion7();
                dbContext.AnimeCast.Add(entry);
            }
        }

        try
        {
            await dbContext.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("23505") == true
                                        || ex.InnerException?.Message.Contains("duplicate key") == true)
        {
            // Concurrent request already inserted — data is in DB, safe to ignore
            dbContext.ChangeTracker.Clear();
        }
    }

    public async Task DeleteByAnimeMalIdAsync(string animeMalId)
        => await dbContext.AnimeCast
            .Where(ac => ac.AnimeMalId == animeMalId)
            .ExecuteDeleteAsync();
}
