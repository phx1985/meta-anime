using MetaAnime.Domain.Enums;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Interfaces.Shared.Results;
using MetaAnime.Domain.Entities;
using MetaAnime.Infrastructure.Kometa.Models;
using MetaAnime.Infrastructure.Kometa.Utilities.Extensions;
using MetaAnime.Infrastructure.Utilities.Extensions;
using System.Collections.Concurrent;

namespace MetaAnime.Application.Services;

public class AnimeMatchingService(
    IKometaAnimeJsonService<KometaRaw> kometaAnimeJsonService,
    IKometaAnimeMappingService kometaAnimeMappingService,
    IAnimeMappingService mappingService,
    IAnimeService animeService)
    : IAnimeMatchingService
{
    public async Task<Result> MatchAsync(bool forceRefreshKometaMapping = false)
    {
        if (forceRefreshKometaMapping)
        {
            var deleteKometaMappingsResult = await kometaAnimeMappingService.BulkDeleteAllAsync();
            if (deleteKometaMappingsResult.IsFailure)
            {
                return Result.Failure(deleteKometaMappingsResult.Error);
            }
        }

        var hasMappingsResult = forceRefreshKometaMapping
            ? Result.Success(false)
            : await kometaAnimeMappingService.AnyAsync();
        if (hasMappingsResult.IsFailure)
        {
            return Result.Failure(hasMappingsResult.Error);
        }

        if (!hasMappingsResult.Value)
        {
            var rawMappingsResult = await kometaAnimeJsonService.GetAllAsync();
            if (rawMappingsResult.IsFailure)
            {
                return Result.Failure(rawMappingsResult.Error);
            }

            var addKometaMappingsResult = await kometaAnimeMappingService.BulkInsertAsync(rawMappingsResult.Value.ToKometaAnimeMappings());
            if (addKometaMappingsResult.IsFailure)
            {
                return Result.Failure(addKometaMappingsResult.Error);
            }
        }

        var refreshDataResult = await animeService.RefreshData();
        if (refreshDataResult.IsFailure)
        {
            return Result.Failure(refreshDataResult.Error);
        }

        var kometaMappingsResult = await kometaAnimeMappingService.GetAllAsync();
        if (kometaMappingsResult.IsFailure)
        {
            return Result.Failure(kometaMappingsResult.Error);
        }

        var kometaMappings = kometaMappingsResult.Value;
        var allAnimes = refreshDataResult.Value;

        var deleteMappingsResult = await mappingService.BulkDeleteAllAsync();
        if (deleteMappingsResult.IsFailure)
        {
            return Result.Failure(deleteMappingsResult.Error);
        }

        var animeMappings = new ConcurrentBag<AnimeMapping>();
        // Platforms are ordered by priority (Tvdb > Imdb > Tmdb)
        var platformMappings = new Dictionary<MediaPlatform, KometaAnimePlatform>
        {
            { MediaPlatform.Tvdb, KometaAnimePlatform.Tvdb },
            { MediaPlatform.Imdb, KometaAnimePlatform.Imdb },
            { MediaPlatform.Tmdb, KometaAnimePlatform.TmdbShow }
        };

        Parallel.ForEach(allAnimes, anime =>
        {
            if (anime.AnimeSource == AnimeSource.MyAnimeList)
            {
                var malKometaMapping = kometaMappings.Where(km => km.GetPlatformEnum() == KometaAnimePlatform.Mal && km.PlatformId == anime.SourceId).ToList();
                malKometaMapping.ForEach(km => animeMappings.Add(new AnimeMapping { AnimeId = anime.Id, AniDbId = km.AniDbId }));
                return;
            }

            if (anime.ExternalAnimeIdentifiers is null || anime.ExternalAnimeIdentifiers.Count == 0)
                return;

            foreach (var platformMapping in platformMappings)
            {
                if (anime.ExternalAnimeIdentifiers.Any(ea => ea.MediaPlatform == platformMapping.Key))
                {
                    var platformId = anime.ExternalAnimeIdentifiers.First(ea => ea.MediaPlatform == platformMapping.Key).MediaPlatformId;
                    var kometaMapping = kometaMappings.Where(km => km.GetPlatformEnum() == platformMapping.Value && km.PlatformId == platformId).ToList();
                    var aniDbIds = kometaMapping.Select(km => km.AniDbId).Distinct().ToList();
                    aniDbIds.ForEach(aniDbId => animeMappings.Add(new AnimeMapping { AnimeId = anime.Id, AniDbId = aniDbId }));
                    break;
                }
            }
        });

        var addMappingsResult = await mappingService.BulkInsertAsync(animeMappings);
        if (addMappingsResult.IsFailure)
        {
            return Result.Failure(addMappingsResult.Error);
        }
        return Result.Success();
    }
}
