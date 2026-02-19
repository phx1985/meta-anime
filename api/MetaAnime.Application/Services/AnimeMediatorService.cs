using MetaAnime.Application.Contracts;
using MetaAnime.Application.Interfaces;
using MetaAnime.Domain.Enums;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Interfaces.Shared.Results;
using MetaAnime.Domain.Entities;
using MetaAnime.Core.Settings;
using MetaAnime.Infrastructure.MyAnimeList.Models;
using MetaAnime.Infrastructure.Plex.Models;
using MetaAnime.Infrastructure.SonarrAPIs.Sonarr.Models;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Text.Json;

namespace MetaAnime.Application.Services
{
    public class AnimeMediatorService(
        IAnimeMappingService animeMappingService,
        IPlexAnimeSourceService<PlexMedia> plexAnimeSource,
        IMalAnimeSourceService<MalAnime> malAnimeSource,
        ISonarrAnimeSourceService<SonarrSeries> sonarrAnimeSource,
        IAnimeMatchingService animeMatchingService,
        IAnimeService animeService,
        ICacheEntryService cacheEntryService,
        IOptions<MalSettings> malOptions,
        IOptions<SonarrSettings> sonarrOptions,
        IOptions<PlexSettings> plexOptions)
        : IAnimeMediatorService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        private readonly string myAnimeListHost = malOptions.Value.WebBaseUrl;
        private readonly string sonarrHost = sonarrOptions.Value.Host;
        // TODO : Add sonarr web url to the configuration

        private readonly string plexBaseUrl = $"{plexOptions.Value.Host}/desktop/#!/server/{plexAnimeSource.GetMachineIdentifierAsync().Result.Value}/details?key=%2Flibrary%2Fmetadata%2F";
        // TODO : Add plex web url to the configuration
        public async Task<Result<PagedResponse<IEnumerable<AnimeDetailsResponse>>>> GetAllAsync(int page = 1, int pageSize = 100)
        {
            try
            {
                var mappingsResult = await animeMappingService.GetAllAsync(includeAnime: true);
                if (mappingsResult.IsFailure)
                {
                    return Result.Failure<PagedResponse<IEnumerable<AnimeDetailsResponse>>>(mappingsResult.Error);
                }

                var allGroupedMappings = mappingsResult.Value
                    .OrderBy(m => m.AniDbId)
                    .ThenBy(m => m.Anime?.AnimeSource)
                    .GroupBy(m => m.AniDbId)
                    .ToList();

                var totalCount = allGroupedMappings.Count;

                var groupedMappings = allGroupedMappings
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                // Load all cache entries for all sources in parallel
                var plexCacheTask = cacheEntryService.GetAsync(AnimeSource.Plex);
                var malCacheTask = cacheEntryService.GetAsync(AnimeSource.MyAnimeList);
                var sonarrCacheTask = cacheEntryService.GetAsync(AnimeSource.Sonarr);

                await Task.WhenAll(plexCacheTask, malCacheTask, sonarrCacheTask);

                var plexCache = plexCacheTask.Result.IsSuccess 
                    ? plexCacheTask.Result.Value.ToDictionary(c => c.SourceId, c => c.RawData) 
                    : new Dictionary<string, string>();
                    
                var malCache = malCacheTask.Result.IsSuccess 
                    ? malCacheTask.Result.Value.ToDictionary(c => c.SourceId, c => c.RawData) 
                    : new Dictionary<string, string>();
                    
                var sonarrCache = sonarrCacheTask.Result.IsSuccess 
                    ? sonarrCacheTask.Result.Value.ToDictionary(c => c.SourceId, c => c.RawData) 
                    : new Dictionary<string, string>();

                var animeDtos = new List<AnimeDetailsResponse>();

                foreach (var group in groupedMappings)
                {
                    var plexAnimes = group
                        .Where(m => m.Anime?.AnimeSource == AnimeSource.Plex)
                        .Select(m => CreatePlexAnimeInfoFromCache(m, plexCache))
                        .ToList();
                    
                    var malAnimes = group
                        .Where(m => m.Anime?.AnimeSource == AnimeSource.MyAnimeList)
                        .Select(m => CreateMalAnimeInfoFromCache(m, malCache))
                        .ToList();
                    
                    var sonarrAnimes = group
                        .Where(m => m.Anime?.AnimeSource == AnimeSource.Sonarr)
                        .Select(m => CreateSonarrAnimeInfoFromCache(m, sonarrCache))
                        .ToList();

                    animeDtos.Add(new AnimeDetailsResponse(
                        group.Key,
                        group.FirstOrDefault()?.Anime?.Title ?? "NO_TITLE",
                        [.. plexAnimes],
                        [.. malAnimes],
                        [.. sonarrAnimes]
                    ));
                }

                var sortedData = animeDtos.OrderBy(a => a.Title);
                var pagedResponse = PagedResponse<IEnumerable<AnimeDetailsResponse>>.Create(
                    sortedData,
                    page,
                    pageSize,
                    totalCount
                );

                return Result.Success(pagedResponse);
            }
            catch (Exception ex)
            {
                return Result.Failure<PagedResponse<IEnumerable<AnimeDetailsResponse>>>(new("Error.GetAllAsync", ex.Message));
            }
        }

        public async Task<Result> SyncAllAsync(bool forceRefreshKometaMapping = false)
        {
            try
            {
                var matchResult = await animeMatchingService.MatchAsync(forceRefreshKometaMapping);
                if (matchResult.IsFailure)
                {
                    return Result.Failure(matchResult.Error);
                }
                return Result.Success();
            }
            catch (Exception ex)
            {
                return Result.Failure(new("Error.SyncAllAsync", ex.Message));
            }
        }

        public async Task<Result<AnimeDetailsResponse?>> GetByIdAsync(string aniDbId)
        {
            try
            {
                var mappingsResult = await animeMappingService.GetAllAsync(includeAnime: true);
                if (mappingsResult.IsFailure)
                {
                    return Result.Failure<AnimeDetailsResponse?>(mappingsResult.Error);
                }

                var animeMappings = mappingsResult.Value
                    .Where(m => m.AniDbId == aniDbId)
                    .OrderBy(m => m.Anime?.AnimeSource)
                    .ToList();

                if (!animeMappings.Any())
                {
                    return Result.Success<AnimeDetailsResponse?>(null);
                }

                var plexAnimes = await FetchPlexAnimeDetailsAsync(animeMappings.Where(m => m.Anime?.AnimeSource == AnimeSource.Plex));
                var malAnimes = await FetchMalAnimeDetailsAsync(animeMappings.Where(m => m.Anime?.AnimeSource == AnimeSource.MyAnimeList));
                var sonarrAnimes = await FetchSonarrAnimeDetailsAsync(animeMappings.Where(m => m.Anime?.AnimeSource == AnimeSource.Sonarr));

                var animeDto = new AnimeDetailsResponse(
                    aniDbId,
                    animeMappings.FirstOrDefault()?.Anime?.Title ?? "NO_TITLE",
                    [.. plexAnimes],
                    [.. malAnimes],
                    [.. sonarrAnimes]
                );

                return Result.Success<AnimeDetailsResponse?>(animeDto);
            }
            catch (Exception ex)
            {
                return Result.Failure<AnimeDetailsResponse?>(new("Error.GetByIdAsync", ex.Message));
            }
        }

        public async Task<Result<AnimeResponse>> CreateAsync(CreateAnimeRequest request)
        {
            try
            {
                var anime = new Anime
                {
                    SourceId = request.SourceId,
                    Title = request.Title,
                    Description = request.Description,
                    AnimeSource = request.AnimeSource
                };

                var insertResult = await animeService.BulkInsertAsync([anime]);
                if (insertResult.IsFailure)
                {
                    return Result.Failure<AnimeResponse>(insertResult.Error);
                }

                return Result.Success(AnimeResponse.FromAnime(anime));
            }
            catch (Exception ex)
            {
                return Result.Failure<AnimeResponse>(new("Error.CreateAsync", ex.Message));
            }
        }

        public async Task<Result<AnimeResponse>> UpdateAsync(Guid id, UpdateAnimeRequest request)
        {
            try
            {
                var animeResult = await animeService.GetByIdAsync(id);
                if (animeResult.IsFailure)
                {
                    return Result.Failure<AnimeResponse>(animeResult.Error);
                }

                var anime = animeResult.Value;
                if (anime == null)
                {
                    return Result.Failure<AnimeResponse>(new("Error.NotFound", "Anime not found"));
                }

                anime.Title = request.Title;
                anime.Description = request.Description;

                var updateResult = await animeService.BulkInsertAsync([anime]);
                if (updateResult.IsFailure)
                {
                    return Result.Failure<AnimeResponse>(updateResult.Error);
                }

                return Result.Success(AnimeResponse.FromAnime(anime));
            }
            catch (Exception ex)
            {
                return Result.Failure<AnimeResponse>(new("Error.UpdateAsync", ex.Message));
            }
        }

        public async Task<Result> DeleteAsync(Guid id)
        {
            try
            {
                var animeResult = await animeService.GetByIdAsync(id);
                if (animeResult.IsFailure)
                {
                    return Result.Failure(animeResult.Error);
                }

                if (animeResult.Value == null)
                {
                    return Result.Failure(new("Error.NotFound", "Anime not found"));
                }

                var deleteResult = await animeService.DeleteAsync(id);
                if (deleteResult.IsFailure)
                {
                    return Result.Failure(deleteResult.Error);
                }

                return Result.Success();
            }
            catch (Exception ex)
            {
                return Result.Failure(new("Error.DeleteAsync", ex.Message));
            }
        }

        #region Private Helper Methods
        private PlexAnimeInfo CreatePlexAnimeInfoFromCache(AnimeMapping mapping, Dictionary<string, string> plexCache)
        {
            if (plexCache.TryGetValue(mapping.Anime!.SourceId, out var rawData))
            {
                try
                {
                    var plexMedia = JsonSerializer.Deserialize<PlexMedia>(rawData, JsonOptions);
                    if (plexMedia != null)
                    {
                        return new PlexAnimeInfo(
                            plexMedia,
                            plexBaseUrl,
                            plexMedia.ViewedLeafCount,
                            plexMedia.LeafCount
                        );
                    }
                }
                catch (JsonException)
                {
                    // Fall through to return basic info
                }
            }
            return new PlexAnimeInfo(mapping.Anime, plexBaseUrl);
        }

        private MalAnimeInfo CreateMalAnimeInfoFromCache(AnimeMapping mapping, Dictionary<string, string> malCache)
        {
            if (malCache.TryGetValue(mapping.Anime!.SourceId, out var rawData))
            {
                try
                {
                    var malAnime = JsonSerializer.Deserialize<MalAnime>(rawData, JsonOptions);
                    if (malAnime != null)
                    {
                        return new MalAnimeInfo(malAnime, myAnimeListHost);
                    }
                }
                catch (JsonException)
                {
                    // Fall through to return basic info
                }
            }
            return new MalAnimeInfo(mapping.Anime, myAnimeListHost);
        }

        private SonarrAnimeInfo CreateSonarrAnimeInfoFromCache(AnimeMapping mapping, Dictionary<string, string> sonarrCache)
        {
            if (sonarrCache.TryGetValue(mapping.Anime!.SourceId, out var rawData))
            {
                try
                {
                    var sonarrSeries = JsonSerializer.Deserialize<SonarrSeries>(rawData, JsonOptions);
                    if (sonarrSeries != null)
                    {
                        // Note: Codec distribution is not included in list view for performance.
                        // It requires an additional API call per series and is only populated in GetById.
                        return new SonarrAnimeInfo(sonarrSeries, sonarrHost, new Dictionary<string, int>());
                    }
                }
                catch (JsonException)
                {
                    // Fall through to return basic info
                }
            }
            return new SonarrAnimeInfo(mapping.Anime, sonarrHost);
        }

        // Keep these methods for GetById which may need real-time data
        private async Task<List<PlexAnimeInfo>> FetchPlexAnimeDetailsAsync(IEnumerable<AnimeMapping> mappings)
        {
            var tasks = mappings.Select(async mapping =>
            {
                var plexResult = await plexAnimeSource.GetByIdAsync(mapping.Anime!.SourceId);
                if (plexResult.IsSuccess && plexResult.Value != null)
                {
                    return new PlexAnimeInfo(
                        plexResult.Value,
                        plexBaseUrl,
                        plexResult.Value.ViewedLeafCount,
                        plexResult.Value.LeafCount
                    );
                }
                else
                {
                    return new PlexAnimeInfo(mapping.Anime, plexBaseUrl);
                }
            });

            return (await Task.WhenAll(tasks)).ToList();
        }

        private async Task<List<MalAnimeInfo>> FetchMalAnimeDetailsAsync(IEnumerable<AnimeMapping> mappings)
        {
            var tasks = mappings.Select(async mapping =>
            {
                var malResult = await malAnimeSource.GetByIdAsync(mapping.Anime!.SourceId);
                if (malResult.IsSuccess && malResult.Value != null)
                {
                    return new MalAnimeInfo(malResult.Value, myAnimeListHost);
                }
                else
                {
                    return new MalAnimeInfo(mapping.Anime, myAnimeListHost);
                }
            });

            return (await Task.WhenAll(tasks)).ToList();
        }

        private async Task<List<SonarrAnimeInfo>> FetchSonarrAnimeDetailsAsync(IEnumerable<AnimeMapping> mappings)
        {
            var tasks = mappings.Select(async mapping =>
            {
                var sonarrResult = await sonarrAnimeSource.GetByIdAsync(mapping.Anime!.SourceId);
                if (sonarrResult.IsSuccess && sonarrResult.Value != null)
                {
                    var codecDistribution = await sonarrAnimeSource.GetCodecDistributionAsync(mapping.Anime!.SourceId);
                    return new SonarrAnimeInfo(sonarrResult.Value, sonarrHost, codecDistribution);
                }
                else
                {
                    return new SonarrAnimeInfo(mapping.Anime, sonarrHost);
                }
            });

            return (await Task.WhenAll(tasks)).ToList();
        }
        #endregion

    }
}
