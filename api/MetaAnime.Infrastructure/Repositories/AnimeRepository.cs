using EFCore.BulkExtensions;
using MetaAnime.Domain.Enums;
using MetaAnime.Core.Interfaces.Mappers;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Domain.Entities;
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
        await dbContext.BulkInsertAsync(animeList);
        await dbContext.BulkInsertAsync(animeList.SelectMany(a => a.ExternalAnimeIdentifiers));
        await dbContext.SaveChangesAsync();
    }

    public async Task BulkInsertAsync(IEnumerable<IAnimeConvertible> animeConvertibles)
    {
        await BulkInsertAsync(animeConvertibles.Select(ac => ac.ToAnime()));
    }

    public async Task BulkDeleteBySourceAsync(AnimeSource animeSource)
    {
        await dbContext.Anime.Where(a => a.AnimeSource == animeSource).ExecuteDeleteAsync();
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
