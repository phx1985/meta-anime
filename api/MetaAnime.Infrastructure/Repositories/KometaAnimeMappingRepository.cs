using EFCore.BulkExtensions;
using MetaAnime.Domain.Enums;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Domain.Entities;
using MetaAnime.Infrastructure.Utilities.Extensions;
using Microsoft.EntityFrameworkCore;

namespace MetaAnime.Infrastructure.Repositories;

public class KometaAnimeMappingRepository(MetaAnimeDbContext dbContext)
    : IKometaAnimeMappingRepository
{
    public async Task<IList<KometaAnimeMapping>> GetAllAsync()
    {
        return await dbContext.KometaAnimeMapping.AsNoTracking().ToListAsync();
    }
    public async Task<IList<KometaAnimeMapping>> GetByAniDbIdAsync(string aniDbId)
    {
        return await dbContext.KometaAnimeMapping
            .AsNoTracking()
            .Where(kam => kam.AniDbId == aniDbId)
            .ToListAsync();
    }
    public async Task<KometaAnimeMapping?> GetByPlatformIdAsync(KometaAnimePlatform animePlatform, string platformId)
    {
        var platformName = animePlatform.GetPlatformName();
        return await dbContext.KometaAnimeMapping
            .AsNoTracking()
            .Where(kam => kam.PlatformName == platformName && kam.PlatformId == platformId)
            .FirstOrDefaultAsync();
    }

    public async Task<IList<KometaAnimeMapping>> GetByPlatformIdsAsync(KometaAnimePlatform animePlatform, IEnumerable<string> platformIds)
    {
        var platformName = animePlatform.GetPlatformName();
        var idList = platformIds.ToList();
        return await dbContext.KometaAnimeMapping
            .AsNoTracking()
            .Where(kam => kam.PlatformName == platformName && idList.Contains(kam.PlatformId))
            .ToListAsync();
    }

    public async Task<IList<KometaAnimeMapping>> GetByAniDbIdsAndPlatformAsync(KometaAnimePlatform animePlatform, IEnumerable<string> aniDbIds)
    {
        var platformName = animePlatform.GetPlatformName();
        var idList = aniDbIds.ToList();
        return await dbContext.KometaAnimeMapping
            .AsNoTracking()
            .Where(kam => kam.PlatformName == platformName && idList.Contains(kam.AniDbId))
            .ToListAsync();
    }
    public async Task<IList<(string MalId, string AniDbId, string? TvdbId)>> GetMalMappingsWithTvdbAsync(IEnumerable<string> malIds)
    {
        var idList = malIds.ToList();
        if (idList.Count == 0) return [];

        var malPlatform = KometaAnimePlatform.Mal.GetPlatformName();   // "mal_id"
        var tvdbPlatform = KometaAnimePlatform.Tvdb.GetPlatformName();  // "tvdb_id"

        // Single LEFT-JOIN query: MAL rows joined with TVDB rows on AniDbId.
        // This is the direct Kometa-as-source-of-truth lookup — no secondary queries needed.
        var rows = await (
            from mal in dbContext.KometaAnimeMapping
            join tvdb in dbContext.KometaAnimeMapping
                on mal.AniDbId equals tvdb.AniDbId into tvdbGroup
            from tvdb in tvdbGroup.Where(t => t.PlatformName == tvdbPlatform).DefaultIfEmpty()
            where mal.PlatformName == malPlatform && idList.Contains(mal.PlatformId)
            select new { MalId = mal.PlatformId, mal.AniDbId, TvdbId = (string?)tvdb.PlatformId }
        )
        .AsNoTracking()
        .ToListAsync();

        // Deduplicate per MalId: prefer the first row that has a non-null TvdbId.
        return rows
            .GroupBy(r => r.MalId, StringComparer.OrdinalIgnoreCase)
            .Select(g => (
                g.Key,
                g.First().AniDbId,
                g.FirstOrDefault(r => r.TvdbId != null)?.TvdbId
            ))
            .ToList<(string, string, string?)>();
    }

    public async Task BulkDeleteAllAsync()
    {
        await dbContext.KometaAnimeMapping.ExecuteDeleteAsync();
    }
    public async Task BulkInsertAsync(IEnumerable<KometaAnimeMapping> kometaAnimeMappings)
    {
        foreach (var kam in kometaAnimeMappings)
        {
            kam.Id = Guid.CreateVersion7();
        }
        await dbContext.BulkInsertAsync(kometaAnimeMappings);
        await dbContext.SaveChangesAsync();
    }

    public async Task<bool> AnyAsync()
    {
        return await dbContext.KometaAnimeMapping.AsNoTracking().AnyAsync();
    }
}
