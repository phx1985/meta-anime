using MetaAnime.Application.Contracts;
using MetaAnime.Application.Interfaces;
using MetaAnime.Application.Queries;
using MetaAnime.Domain.Enums;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Interfaces.Shared.Results;
using MetaAnime.Domain.Entities;
using MetaAnime.Core.Settings;
using MetaAnime.Infrastructure.MyAnimeList.Models;
using MetaAnime.Infrastructure.Plex.Models;
using MetaAnime.Infrastructure.SonarrAPIs.Sonarr.Models;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
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
        IOptions<PlexSettings> plexOptions,
        ILogger<AnimeMediatorService> logger)
        : IAnimeMediatorService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
            // Don't use DefaultIgnoreCondition to match serialization options
        };

        private readonly string myAnimeListHost = malOptions.Value.WebBaseUrl;
        private readonly string sonarrSeriesBaseUrl = !string.IsNullOrWhiteSpace(sonarrOptions.Value.RemoteUrl)
            ? sonarrOptions.Value.RemoteUrl
            : sonarrOptions.Value.Host;

        private readonly string plexBaseUrl = $"{plexOptions.Value.Host}/desktop/#!/server/{plexAnimeSource.GetMachineIdentifierAsync().Result.Value}/details?key=%2Flibrary%2Fmetadata%2F";

        public async Task<Result<PagedResponse<IEnumerable<AnimeDetailsResponse>>>> GetAllAsync(int page = 1, int pageSize = 100, AnimeFilters? filters = null, bool enriched = false)
        {
            try
            {
                var mappingsResult = await animeMappingService.GetAllAsync(includeAnime: true);
                if (mappingsResult.IsFailure)
                {
                    return Result.Failure<PagedResponse<IEnumerable<AnimeDetailsResponse>>>(mappingsResult.Error);
                }

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

                // Group all mappings
                var allGroupedMappings = mappingsResult.Value
                    .OrderBy(m => m.AniDbId)
                    .ThenBy(m => m.Anime?.AnimeSource)
                    .GroupBy(m => m.AniDbId)
                    .ToList();

                var animeDtos = new List<AnimeDetailsResponse>();

                foreach (var group in allGroupedMappings)
                {
                    List<PlexAnimeInfo> plexAnimes;
                    List<MalAnimeInfo> malAnimes;
                    List<SonarrAnimeInfo> sonarrAnimes;

                    if (enriched)
                    {
                        var plexTask = FetchPlexAnimeDetailsAsync(group.Where(m => m.Anime?.AnimeSource == AnimeSource.Plex));
                        var malTask = FetchMalAnimeDetailsAsync(group.Where(m => m.Anime?.AnimeSource == AnimeSource.MyAnimeList));
                        var sonarrTask = FetchSonarrAnimeDetailsAsync(group.Where(m => m.Anime?.AnimeSource == AnimeSource.Sonarr));
                        await Task.WhenAll(plexTask, malTask, sonarrTask);
                        plexAnimes = plexTask.Result;
                        malAnimes = malTask.Result;
                        sonarrAnimes = sonarrTask.Result;
                    }
                    else
                    {
                        plexAnimes = group
                            .Where(m => m.Anime?.AnimeSource == AnimeSource.Plex)
                            .Select(m => CreatePlexAnimeInfoFromCache(m, plexCache))
                            .ToList();

                        malAnimes = group
                            .Where(m => m.Anime?.AnimeSource == AnimeSource.MyAnimeList)
                            .Select(m => CreateMalAnimeInfoFromCache(m, malCache))
                            .ToList();

                        sonarrAnimes = group
                            .Where(m => m.Anime?.AnimeSource == AnimeSource.Sonarr)
                            .Select(m => CreateSonarrAnimeInfoFromCache(m, sonarrCache))
                            .ToList();
                    }

                    animeDtos.Add(new AnimeDetailsResponse(
                        group.Key,
                        group.FirstOrDefault()?.Anime?.Title ?? "NO_TITLE",
                        [.. plexAnimes],
                        [.. malAnimes],
                        [.. sonarrAnimes]
                    ));
                }

                // Apply filters (pass malCache for genre filtering)
                var filteredData = ApplyFilters(animeDtos, filters, malCache);

                // Apply sorting
                var sortedData = ApplySorting(filteredData, filters);

                // Calculate total count after filtering
                var totalCount = sortedData.Count();

                // Apply pagination
                var pagedData = sortedData
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize);

                var pagedResponse = PagedResponse<IEnumerable<AnimeDetailsResponse>>.Create(
                    pagedData,
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

        private IEnumerable<AnimeDetailsResponse> ApplyFilters(
            IEnumerable<AnimeDetailsResponse> data, 
            AnimeFilters? filters,
            Dictionary<string, string> malCache)
        {
            if (filters == null)
                return data;

            var result = data;

            if (!string.IsNullOrWhiteSpace(filters.Title))
            {
                result = result.Where(a => a.Title.Contains(filters.Title, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(filters.Source))
            {
                result = result.Where(a =>
                    (filters.Source.Equals("plex", StringComparison.OrdinalIgnoreCase) && a.PlexAnimes?.Any() == true) ||
                    (filters.Source.Equals("mal", StringComparison.OrdinalIgnoreCase) && a.MalAnimes?.Any() == true) ||
                    (filters.Source.Equals("sonarr", StringComparison.OrdinalIgnoreCase) && a.SonarrAnimes?.Any() == true)
                );
            }

            if (!string.IsNullOrWhiteSpace(filters.Status))
            {
                result = result.Where(a => 
                    a.MalAnimes?.Any(m => m.ViewState != null && m.ViewState.Contains(filters.Status, StringComparison.OrdinalIgnoreCase)) == true ||
                    a.SonarrAnimes?.Any(s => s.Exists) == true
                );
            }

            if (!string.IsNullOrWhiteSpace(filters.Genre))
            {
                // Filter by MAL genre - check cached MAL data for genre names
                // Note: Deserializes MAL JSON for each anime when genre filter is applied
                // This is acceptable because: 1) Genre filtering is optional, 2) Only applied to
                // anime that passed previous filters, 3) Avoids memory overhead of caching deserialized objects
                var genreFilter = filters.Genre;
                result = result.Where(a =>
                {
                    if (a.MalAnimes == null || !a.MalAnimes.Any())
                        return false;
                    
                    // Check if any MAL anime in this group has the specified genre
                    return a.MalAnimes.Any(m =>
                    {
                        if (string.IsNullOrWhiteSpace(m.Id))
                            return false;
                        
                        // Get cached MAL anime JSON and deserialize to check genres
                        if (!malCache.TryGetValue(m.Id, out var malJson) || string.IsNullOrWhiteSpace(malJson))
                            return false;
                        
                        try
                        {
                            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                            var malAnime = JsonSerializer.Deserialize<MalAnime>(malJson, options);
                            return malAnime?.Genres?.Any(g => 
                                g.Name.Contains(genreFilter, StringComparison.OrdinalIgnoreCase)
                            ) == true;
                        }
                        catch (JsonException ex)
                        {
                            logger.LogWarning("Failed to deserialize MAL anime {MalId} for genre filtering: {Error}", m.Id, ex.Message);
                            return false;
                        }
                    });
                });
            }

            return result;
        }

        private IEnumerable<AnimeDetailsResponse> ApplySorting(IEnumerable<AnimeDetailsResponse> data, AnimeFilters? filters)
        {
            if (filters == null || string.IsNullOrWhiteSpace(filters.SortBy))
                return data.OrderBy(a => a.Title);

            var sortOrder = filters.SortOrder?.Equals("desc", StringComparison.OrdinalIgnoreCase) == true;

            return filters.SortBy.ToLower() switch
            {
                "title" => sortOrder ? data.OrderByDescending(a => a.Title) : data.OrderBy(a => a.Title),
                "anidbid" => sortOrder ? data.OrderByDescending(a => a.AniDbId) : data.OrderBy(a => a.AniDbId),
                _ => data.OrderBy(a => a.Title)
            };
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
            if (mapping.Anime?.SourceId != null && malCache.TryGetValue(mapping.Anime.SourceId, out var rawData))
            {
                if (!string.IsNullOrWhiteSpace(rawData))
                {
                    try
                    {
                        var malAnime = JsonSerializer.Deserialize<MalAnime>(rawData, JsonOptions);
                        if (malAnime != null)
                        {
                            // Log for debugging - check if MyListStatus is null
                            if (malAnime.MyListStatus == null)
                            {
                                logger.LogWarning("MAL anime {SourceId} ({Title}) has null MyListStatus after deserialization", 
                                    mapping.Anime.SourceId, malAnime.Title);
                            }
                            else
                            {
                                logger.LogInformation("MAL anime {SourceId} ({Title}) has MyListStatus: Status={Status}, CreatedAt={CreatedAt}", 
                                    mapping.Anime.SourceId, malAnime.Title, malAnime.MyListStatus.Status, malAnime.MyListStatus.CreatedAt);
                            }
                            return new MalAnimeInfo(malAnime, myAnimeListHost);
                        }
                    }
                    catch (JsonException ex)
                    {
                        // Log deserialization error for debugging
                        logger.LogWarning(ex, "Failed to deserialize MAL anime cache data for SourceId: {SourceId}", mapping.Anime.SourceId);
                    }
                }
            }
            // Fallback to basic anime info (will have null detailed fields)
            return new MalAnimeInfo(mapping.Anime, myAnimeListHost);
        }

        private SonarrAnimeInfo CreateSonarrAnimeInfoFromCache(AnimeMapping mapping, Dictionary<string, string> sonarrCache)
        {
            if (mapping.Anime?.SourceId != null && sonarrCache.TryGetValue(mapping.Anime.SourceId, out var rawData))
            {
                if (!string.IsNullOrWhiteSpace(rawData))
                {
                    try
                    {
                        var sonarrSeries = JsonSerializer.Deserialize<SonarrSeries>(rawData, JsonOptions);
                        if (sonarrSeries != null)
                        {
                            // Log for debugging - check if Seasons is null
                            if (sonarrSeries.Seasons == null || !sonarrSeries.Seasons.Any())
                            {
                                logger.LogWarning("Sonarr anime {SourceId} ({Title}) has null or empty Seasons after deserialization", 
                                    mapping.Anime.SourceId, sonarrSeries.Title);
                            }
                            else
                            {
                                var seasonsWithStats = sonarrSeries.Seasons.Count(s => s.Statistics != null);
                                logger.LogInformation("Sonarr anime {SourceId} ({Title}) has {TotalSeasons} seasons, {SeasonsWithStats} with statistics", 
                                    mapping.Anime.SourceId, sonarrSeries.Title, sonarrSeries.Seasons.Count, seasonsWithStats);
                            }
                            // Note: Codec distribution is not included in list view for performance.
                            // It requires an additional API call per series and is only populated in GetById.
                            // Size on disk is null in list view as it's populated in GetById with real-time data
                            return new SonarrAnimeInfo(sonarrSeries, sonarrSeriesBaseUrl, new Dictionary<string, int>());
                        }
                    }
                    catch (JsonException ex)
                    {
                        // Log deserialization error for debugging
                        logger.LogWarning(ex, "Failed to deserialize Sonarr anime cache data for SourceId: {SourceId}", mapping.Anime.SourceId);
                    }
                }
            }
            // Fallback to basic anime info (will have null detailed fields)
            return new SonarrAnimeInfo(mapping.Anime, sonarrSeriesBaseUrl);
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
                    return new SonarrAnimeInfo(sonarrResult.Value, sonarrSeriesBaseUrl, codecDistribution);
                }
                else
                {
                    return new SonarrAnimeInfo(mapping.Anime, sonarrSeriesBaseUrl);
                }
            });

            return (await Task.WhenAll(tasks)).ToList();
        }
        #endregion

    }
}
