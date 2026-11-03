using MetaAnime.Domain.Enums;
using MetaAnime.Core.Interfaces.Mappers;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Domain.Entities;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace MetaAnime.Infrastructure.Repositories;

public sealed class AnimeRepository(MetaAnimeDbContext dbContext) : IAnimeRepository
{
    public async Task AddAsync(Anime anime)
    {
        anime.Id = Guid.CreateVersion7();
        // Let EF Core track ExternalAnimeIdentifiers via navigation property
        await dbContext.Anime.AddAsync(anime);
        await dbContext.SaveChangesAsync();
    }

    public async Task BulkInsertAsync(IEnumerable<Anime> animes)
    {
        var animeList = animes.ToList();
        foreach (var anime in animeList)
        {
            anime.Id = Guid.CreateVersion7();
            foreach (var externalAnimeIdentifier in anime.ExternalAnimeIdentifiers)
            {
                externalAnimeIdentifier.Id = Guid.CreateVersion7();
                externalAnimeIdentifier.AnimeId = anime.Id;
            }
        }
        await dbContext.BulkCopyAsync(animeList);
        await dbContext.BulkCopyAsync(animeList.SelectMany(a => a.ExternalAnimeIdentifiers).ToList());
    }

    public async Task BulkInsertAsync(IEnumerable<IAnimeConvertible> animeConvertibles)
    {
        await BulkInsertAsync(animeConvertibles.Select(ac => ac.ToAnime()));
    }

    public async Task BulkDeleteBySourceAsync(AnimeSource animeSource)
    {
        await dbContext.Anime.Where(a => a.AnimeSource == animeSource).ExecuteDeleteAsync();
    }

    /// <inheritdoc />
    public async Task MergeBySourceAsync(AnimeSource animeSource, IEnumerable<Anime> incoming)
    {
        var incomingList = incoming.ToList();
        var existing = await dbContext.Anime
            .Include(a => a.ExternalAnimeIdentifiers)
            .Where(a => a.AnimeSource == animeSource)
            .ToListAsync();

        // Keyed on (AnimeSource, SourceId) — source is fixed for this call.
        var existingBySourceId = existing
            .GroupBy(a => a.SourceId)
            .ToDictionary(g => g.Key, g => g.First());
        var matchedSourceIds = new HashSet<string>(StringComparer.Ordinal);
        var toInsert = new List<Anime>();
        var toUpdate = new List<(Anime Existing, Anime Incoming)>();

        foreach (var inc in incomingList)
        {
            matchedSourceIds.Add(inc.SourceId);
            if (!existingBySourceId.TryGetValue(inc.SourceId, out var ex))
            {
                toInsert.Add(inc);
                continue;
            }

            if (!HasSameShape(ex, inc))
                toUpdate.Add((ex, inc));
        }

        // Tombstone rows missing from the incoming set (cascades identifiers + mappings).
        var staleIds = existing
            .Where(a => !matchedSourceIds.Contains(a.SourceId))
            .Select(a => a.Id)
            .ToList();
        if (staleIds.Count > 0)
        {
            await dbContext.Anime.Where(a => staleIds.Contains(a.Id)).ExecuteDeleteAsync();
        }

        // Update changed rows in place so existing ids (and their FKs) stay stable.
        // Children live in a tracked collection: Clear() deletes the old identifiers and
        // adding the new ones inserts them in the same SaveChanges.
        foreach (var (ex, inc) in toUpdate)
        {
            ex.Title = inc.Title;
            ex.Description = inc.Description;

            ex.ExternalAnimeIdentifiers.Clear();
            foreach (var identifier in inc.ExternalAnimeIdentifiers)
            {
                identifier.Id = Guid.CreateVersion7();
                identifier.AnimeId = ex.Id;
                ex.ExternalAnimeIdentifiers.Add(identifier);
            }
        }

        if (toUpdate.Count > 0)
            await dbContext.SaveChangesAsync();

        if (toInsert.Count > 0)
            await BulkInsertAsync(toInsert);
    }

    private static bool HasSameShape(Anime existing, Anime incoming)
    {
        if (!string.Equals(existing.Title, incoming.Title, StringComparison.Ordinal)
            || !string.Equals(existing.Description ?? string.Empty, incoming.Description ?? string.Empty, StringComparison.Ordinal))
            return false;

        static HashSet<(Domain.Enums.MediaPlatform, string)> Shape(Anime a) =>
            [.. a.ExternalAnimeIdentifiers.Select(i => (i.MediaPlatform, i.MediaPlatformId))];

        return Shape(existing).SetEquals(Shape(incoming));
    }

    public async Task BulkDeleteAllAsync()
    {
        await dbContext.Anime.ExecuteDeleteAsync();
    }

    public async Task<IEnumerable<Anime>> GetAllBySourceAsync(AnimeSource source)
    {
        return await dbContext.Anime.AsNoTracking().Where(a => a.AnimeSource == source).ToListAsync();
    }

    public async Task<IEnumerable<Anime>> GetAllAsync()
    {
        return await dbContext.Anime.AsNoTracking()
                                    .Include(a => a.ExternalAnimeIdentifiers)
                                    .ToListAsync();
    }

    public async Task<IEnumerable<Anime>> GetManyBySourceIdsAsync(AnimeSource source, IEnumerable<string> sourceIds)
    {
        var ids = sourceIds.ToList();
        return await dbContext.Anime
            .AsNoTracking()
            .Where(a => a.AnimeSource == source && ids.Contains(a.SourceId))
            .ToListAsync();
    }

    public async Task<Anime?> GetAnimeAsync(AnimeSource source, string sourceId)
    {
        return await dbContext.Anime.AsNoTracking().FirstOrDefaultAsync(a => a.SourceId == sourceId && a.AnimeSource == source);
    }

    public async Task<Anime?> GetByIdAsync(Guid id)
    {
        return await dbContext.Anime.AsNoTracking()
            .Include(a => a.ExternalAnimeIdentifiers)
            .FirstOrDefaultAsync(a => a.Id == id);
    }

    public async Task DeleteAsync(Guid id)
    {
        await dbContext.Anime.Where(a => a.Id == id).ExecuteDeleteAsync();
    }
}
