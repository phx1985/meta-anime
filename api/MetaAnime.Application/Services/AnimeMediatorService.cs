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
        private readonly string myAnimeListHost = malOptions.Value.WebBaseUrl;
        private readonly string sonarrHost = sonarrOptions.Value.Host;
        // TODO : Add sonarr web url to the configuration

        private readonly string plexBaseUrl = $"{plexOptions.Value.Host}/desktop/#!/server/{plexAnimeSource.GetMachineIdentifierAsync().Result.Value}/details?key=%2Flibrary%2Fmetadata%2F";
        // TODO : Add plex web url to the configuration
        public async Task<Result<IEnumerable<AnimeDetailsResponse>>> GetAllAsync(int page = 1, int pageSize = 100)
        {
            try
            {
                var mappingsResult = await animeMappingService.GetAllAsync(includeAnime: true);
                if (mappingsResult.IsFailure)
                {
                    return Result.Failure<IEnumerable<AnimeDetailsResponse>>(mappingsResult.Error);
                }

                var groupedMappings = mappingsResult.Value
                    .OrderBy(m => m.AniDbId)
                    .ThenBy(m => m.Anime?.AnimeSource)
                    .GroupBy(m => m.AniDbId)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                // Pre-fetch all external data in bulk to avoid N+1 queries
                var plexIds = groupedMappings
                    .SelectMany(g => g.Where(m => m.Anime?.AnimeSource == AnimeSource.Plex))
                    .Select(m => m.Anime!.SourceId)
                    .Distinct()
                    .ToList();
                
                var malIds = groupedMappings
                    .SelectMany(g => g.Where(m => m.Anime?.AnimeSource == AnimeSource.MyAnimeList))
                    .Select(m => m.Anime!.SourceId)
                    .Distinct()
                    .ToList();
                
                var sonarrIds = groupedMappings
                    .SelectMany(g => g.Where(m => m.Anime?.AnimeSource == AnimeSource.Sonarr))
                    .Select(m => m.Anime!.SourceId)
                    .Distinct()
                    .ToList();

                // Fetch all data in parallel
                var plexDataTask = FetchPlexDataBulkAsync(plexIds);
                var malDataTask = FetchMalDataBulkAsync(malIds);
                var sonarrDataTask = FetchSonarrDataBulkAsync(sonarrIds);

                await Task.WhenAll(plexDataTask, malDataTask, sonarrDataTask);

                var plexData = plexDataTask.Result;
                var malData = malDataTask.Result;
                var sonarrData = sonarrDataTask.Result;

                var animeDtos = new List<AnimeDetailsResponse>();

                foreach (var group in groupedMappings)
                {
                    var plexAnimes = group
                        .Where(m => m.Anime?.AnimeSource == AnimeSource.Plex)
                        .Select(m => CreatePlexAnimeInfo(m, plexData))
                        .ToList();
                    
                    var malAnimes = group
                        .Where(m => m.Anime?.AnimeSource == AnimeSource.MyAnimeList)
                        .Select(m => CreateMalAnimeInfo(m, malData))
                        .ToList();
                    
                    var sonarrAnimes = group
                        .Where(m => m.Anime?.AnimeSource == AnimeSource.Sonarr)
                        .Select(m => CreateSonarrAnimeInfo(m, sonarrData))
                        .ToList();

                    animeDtos.Add(new AnimeDetailsResponse(
                        group.Key,
                        group.FirstOrDefault()?.Anime?.Title ?? "NO_TITLE",
                        [.. plexAnimes],
                        [.. malAnimes],
                        [.. sonarrAnimes]
                    ));
                }

                return Result.Success<IEnumerable<AnimeDetailsResponse>>(animeDtos.OrderBy(a => a.Title));
            }
            catch (Exception ex)
            {
                return Result.Failure<IEnumerable<AnimeDetailsResponse>>(new("Error.GetAllAsync", ex.Message));
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
        private async Task<Dictionary<string, PlexMedia>> FetchPlexDataBulkAsync(List<string> plexIds)
        {
            var result = new Dictionary<string, PlexMedia>();
            if (!plexIds.Any()) return result;

            var tasks = plexIds.Select(async id =>
            {
                var plexResult = await plexAnimeSource.GetByIdAsync(id);
                if (plexResult.IsSuccess && plexResult.Value != null)
                {
                    return (id, (PlexMedia?)plexResult.Value);
                }
                return (id, (PlexMedia?)null);
            });

            var results = await Task.WhenAll(tasks);
            foreach (var (id, media) in results)
            {
                if (media != null)
                {
                    result[id] = media;
                }
            }

            return result;
        }

        private async Task<Dictionary<string, MalAnime>> FetchMalDataBulkAsync(List<string> malIds)
        {
            var result = new Dictionary<string, MalAnime>();
            if (!malIds.Any()) return result;

            // Try to get from cache first
            var cacheResult = await cacheEntryService.GetAsync(AnimeSource.MyAnimeList);
            var cachedData = new Dictionary<string, MalAnime>();
            
            if (cacheResult.IsSuccess)
            {
                foreach (var entry in cacheResult.Value)
                {
                    try
                    {
                        var malAnime = System.Text.Json.JsonSerializer.Deserialize<MalAnime>(entry.RawData);
                        if (malAnime != null)
                        {
                            cachedData[entry.SourceId] = malAnime;
                        }
                    }
                    catch { /* Skip invalid cache entries */ }
                }
            }

            // Fetch missing data
            var missingIds = malIds.Where(id => !cachedData.ContainsKey(id)).ToList();
            if (missingIds.Any())
            {
                var tasks = missingIds.Select(async id =>
                {
                    var malResult = await malAnimeSource.GetByIdAsync(id);
                    if (malResult.IsSuccess && malResult.Value != null)
                    {
                        return (id, (MalAnime?)malResult.Value);
                    }
                    return (id, (MalAnime?)null);
                });

                var results = await Task.WhenAll(tasks);
                foreach (var (id, anime) in results)
                {
                    if (anime != null)
                    {
                        cachedData[id] = anime;
                    }
                }
            }

            return cachedData;
        }

        private async Task<Dictionary<string, (SonarrSeries series, Dictionary<string, int>? codecDist)>> FetchSonarrDataBulkAsync(List<string> sonarrIds)
        {
            var result = new Dictionary<string, (SonarrSeries series, Dictionary<string, int>? codecDist)>();
            if (!sonarrIds.Any()) return result;

            var tasks = sonarrIds.Select(async id =>
            {
                var sonarrResult = await sonarrAnimeSource.GetByIdAsync(id);
                if (sonarrResult.IsSuccess && sonarrResult.Value != null)
                {
                    // Don't fetch codec distribution here - it's too slow
                    return (id, (SonarrSeries?)sonarrResult.Value, (Dictionary<string, int>?)null);
                }
                return (id, (SonarrSeries?)null, (Dictionary<string, int>?)null);
            });

            var results = await Task.WhenAll(tasks);
            foreach (var (id, series, codecDist) in results)
            {
                if (series != null)
                {
                    result[id] = (series, codecDist);
                }
            }

            return result;
        }

        private PlexAnimeInfo CreatePlexAnimeInfo(AnimeMapping mapping, Dictionary<string, PlexMedia> plexData)
        {
            if (plexData.TryGetValue(mapping.Anime!.SourceId, out var plexMedia))
            {
                return new PlexAnimeInfo(
                    plexMedia,
                    plexBaseUrl,
                    plexMedia.ViewedLeafCount,
                    plexMedia.LeafCount
                );
            }
            return new PlexAnimeInfo(mapping.Anime, plexBaseUrl);
        }

        private MalAnimeInfo CreateMalAnimeInfo(AnimeMapping mapping, Dictionary<string, MalAnime> malData)
        {
            if (malData.TryGetValue(mapping.Anime!.SourceId, out var malAnime))
            {
                return new MalAnimeInfo(malAnime, myAnimeListHost);
            }
            return new MalAnimeInfo(mapping.Anime, myAnimeListHost);
        }

        private SonarrAnimeInfo CreateSonarrAnimeInfo(AnimeMapping mapping, Dictionary<string, (SonarrSeries series, Dictionary<string, int>? codecDist)> sonarrData)
        {
            if (sonarrData.TryGetValue(mapping.Anime!.SourceId, out var data))
            {
                return new SonarrAnimeInfo(data.series, sonarrHost, data.codecDist ?? new Dictionary<string, int>());
            }
            return new SonarrAnimeInfo(mapping.Anime, sonarrHost);
        }

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
