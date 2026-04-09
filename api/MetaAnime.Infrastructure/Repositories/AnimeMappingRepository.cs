using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Domain.Entities;
using MetaAnime.Domain.Enums;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace MetaAnime.Infrastructure.Repositories;

public sealed class AnimeMappingRepository(MetaAnimeDbContext dbContext) : IAnimeMappingRepository
{
    public async Task BulkDeleteAllAsync()
    {
        await dbContext.AnimeMappings.ExecuteDeleteAsync();
    }

    public async Task BulkInsertAsync(IEnumerable<AnimeMapping> mappings)
    {
        var mappingList = mappings.ToList();
        foreach (var mapping in mappingList)
            mapping.Id = Guid.CreateVersion7();

        await dbContext.BulkCopyAsync(mappingList);
    }

    public async Task AddAsync(AnimeMapping mapping)
    {
        mapping.Id = Guid.CreateVersion7();
        await dbContext.AnimeMappings.AddAsync(mapping);
        await dbContext.SaveChangesAsync();
    }

    public async Task<IEnumerable<AnimeMapping>> GetAllAsync(bool includeAnime = false)
    {
        return includeAnime
            ? await dbContext.AnimeMappings.AsNoTracking().Include(am => am.Anime).ToListAsync()
            : await dbContext.AnimeMappings.AsNoTracking().ToListAsync();
    }

    public async Task<(List<string> AniDbIds, int TotalCount)> GetPagedAniDbIdsAsync(
        int page, int pageSize,
        string? titleFilter = null,
        AnimeSource? sourceFilter = null,
        string? sortBy = null,
        bool sortDescending = false)
    {
        IQueryable<AnimeMapping> baseQuery = dbContext.AnimeMappings.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(titleFilter))
            baseQuery = baseQuery.Where(am => EF.Functions.ILike(am.Anime!.Title, $"%{titleFilter}%"));

        if (sourceFilter.HasValue)
            baseQuery = baseQuery.Where(am => am.Anime!.AnimeSource == sourceFilter.Value);

        // Group to distinct AniDbIds with a representative title for sorting
        var grouped = baseQuery
            .GroupBy(am => am.AniDbId)
            .Select(g => new { AniDbId = g.Key, Title = g.Min(am => am.Anime!.Title) });

        int totalCount = await grouped.CountAsync();

        List<string> aniDbIds;
        if (sortBy?.Equals("anidbid", StringComparison.OrdinalIgnoreCase) == true)
        {
            var ordered = sortDescending
                ? grouped.OrderByDescending(g => g.AniDbId)
                : grouped.OrderBy(g => g.AniDbId);
            aniDbIds = await ordered.Skip((page - 1) * pageSize).Take(pageSize).Select(g => g.AniDbId).ToListAsync();
        }
        else
        {
            var ordered = sortDescending
                ? grouped.OrderByDescending(g => g.Title)
                : grouped.OrderBy(g => g.Title);
            aniDbIds = await ordered.Skip((page - 1) * pageSize).Take(pageSize).Select(g => g.AniDbId).ToListAsync();
        }

        return (aniDbIds, totalCount);
    }

    public async Task<List<string>> GetAllFilteredAniDbIdsAsync(
        string? titleFilter = null,
        AnimeSource? sourceFilter = null)
    {
        IQueryable<AnimeMapping> baseQuery = dbContext.AnimeMappings.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(titleFilter))
            baseQuery = baseQuery.Where(am => EF.Functions.ILike(am.Anime!.Title, $"%{titleFilter}%"));

        if (sourceFilter.HasValue)
            baseQuery = baseQuery.Where(am => am.Anime!.AnimeSource == sourceFilter.Value);

        return await baseQuery
            .Select(am => am.AniDbId)
            .Distinct()
            .ToListAsync();
    }

    public async Task<IEnumerable<AnimeMapping>> GetByAniDbIdsAsync(IEnumerable<string> aniDbIds)
    {
        List<string> idList = [..aniDbIds];
        return await dbContext.AnimeMappings
            .AsNoTracking()
            .Include(am => am.Anime)
            .Where(am => idList.Contains(am.AniDbId))
            .ToListAsync();
    }

    public async Task<string?> GetAniDbIdByMalIdAsync(string malId)
    {
        return await dbContext.AnimeMappings
            .AsNoTracking()
            .Include(am => am.Anime)
            .Where(am => am.Anime!.AnimeSource == AnimeSource.MyAnimeList && am.Anime.SourceId == malId)
            .Select(am => am.AniDbId)
            .FirstOrDefaultAsync();
    }

    public async Task DeleteByAniDbIdAsync(string aniDbId, string malId)
    {
        await dbContext.AnimeMappings
            .Where(am => am.AniDbId == aniDbId
                && am.Anime!.AnimeSource == AnimeSource.MyAnimeList
                && am.Anime.SourceId == malId)
            .ExecuteDeleteAsync();
    }
}
