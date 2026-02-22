using MetaAnime.Application.Contracts;
using MetaAnime.Application.Interfaces;
using MetaAnime.Application.Queries;
using MetaAnime.Domain.Enums;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Interfaces.Shared.Results;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Domain.Entities;
using MetaAnime.Core.Settings;
using MetaAnime.Infrastructure.MyAnimeList.Models;
using MetaAnime.Infrastructure.Plex.Models;
using MetaAnime.Infrastructure.SonarrAPIs.Sonarr.Models;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.Json;

namespace MetaAnime.Application.Services;

public class AnimeMediatorService(
    IAnimeMappingService animeMappingService,
    IPlexAnimeSourceService<PlexMedia> plexAnimeSource,
    IMalAnimeSourceService<MalAnime> malAnimeSource,
    ISonarrAnimeSourceService<SonarrSeries> sonarrAnimeSource,
    IAnimeMatchingService animeMatchingService,
    IAnimeService animeService,
    ICacheEntryService cacheEntryService,
    IPropertyBagRepository propertyBagRepository,
    IOptions<MalSettings> malOptions,
    IOptions<SonarrSettings> sonarrOptions,
    IOptions<PlexSettings> plexOptions,
    ILogger<AnimeMediatorService> logger)
    : IAnimeMediatorService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string myAnimeListHost = malOptions.Value.WebBaseUrl;
    private readonly string sonarrSeriesBaseUrl = !string.IsNullOrWhiteSpace(sonarrOptions.Value.RemoteUrl)
        ? sonarrOptions.Value.RemoteUrl
        : sonarrOptions.Value.Host;

    private readonly string plexBaseUrl = $"{plexOptions.Value.Host}/web/index.html#!/server/{plexAnimeSource.GetMachineIdentifierAsync().Result.Value}/details?key=%2Flibrary%2Fmetadata%2F";

    public async Task<Result<PagedResponse<IEnumerable<AnimeDetailsResponse>>>> GetAllAsync(int page = 1, int pageSize = 100, AnimeFilters? filters = null, bool enriched = false)
    {
        try
        {
            bool showNsfw = await IsNsfwAllowedAsync();

            AnimeSource? sourceFilter = filters?.Source?.ToLower() switch
            {
                "plex" => AnimeSource.Plex,
                "mal" => AnimeSource.MyAnimeList,
                "sonarr" => AnimeSource.Sonarr,
                _ => null
            };
            bool sortDescending = filters?.SortOrder?.Equals("desc", StringComparison.OrdinalIgnoreCase) == true;
            bool needsInMemoryPostFilter = !string.IsNullOrWhiteSpace(filters?.Genre)
                                         || !string.IsNullOrWhiteSpace(filters?.Status)
                                         || !showNsfw;

            List<string> aniDbIds;
            int totalCount;

            if (!needsInMemoryPostFilter)
            {
                // FAST PATH: push pagination + filtering entirely into the database.
                // Two targeted SQL queries (COUNT + paged IDs) replace a full-table scan.
                var pagedResult = await animeMappingService.GetPagedAniDbIdsAsync(
                    page, pageSize, filters?.Title, sourceFilter, filters?.SortBy, sortDescending);
                if (pagedResult.IsFailure)
                    return Result.Failure<PagedResponse<IEnumerable<AnimeDetailsResponse>>>(pagedResult.Error);

                (aniDbIds, totalCount) = pagedResult.Value;

                if (aniDbIds.Count == 0)
                    return Result.Success(PagedResponse<IEnumerable<AnimeDetailsResponse>>.Create(
                        Enumerable.Empty<AnimeDetailsResponse>(), page, pageSize, 0));
            }
            else
            {
                // SLOWER PATH: genre/status require in-memory post-filtering.
                // Still efficient: only load the title/source-filtered subset of AniDbIds (string IDs, no JSON).
                var filteredResult = await animeMappingService.GetAllFilteredAniDbIdsAsync(
                    filters?.Title, sourceFilter);
                if (filteredResult.IsFailure)
                    return Result.Failure<PagedResponse<IEnumerable<AnimeDetailsResponse>>>(filteredResult.Error);

                aniDbIds = filteredResult.Value;
                totalCount = 0; // will be recalculated after in-memory filter
            }

            var mappingsResult = await animeMappingService.GetByAniDbIdsAsync(aniDbIds);
            if (mappingsResult.IsFailure)
                return Result.Failure<PagedResponse<IEnumerable<AnimeDetailsResponse>>>(mappingsResult.Error);

            var mappings = mappingsResult.Value.ToList();

            var sourceIds = mappings
                .Where(m => m.Anime != null)
                .Select(m => m.Anime!.SourceId)
                .Distinct()
                .ToList();

            var cacheResult = await cacheEntryService.GetBySourceIdsAsync(sourceIds);
            var cacheEntries = cacheResult.IsSuccess ? cacheResult.Value.ToList() : new List<CacheEntry>();

            var plexCache = cacheEntries
                .Where(c => c.AnimeSource == AnimeSource.Plex)
                .GroupBy(c => c.SourceId).ToDictionary(g => g.Key, g => g.Last().RawData);
            var malCache = cacheEntries
                .Where(c => c.AnimeSource == AnimeSource.MyAnimeList)
                .GroupBy(c => c.SourceId).ToDictionary(g => g.Key, g => g.Last().RawData);
            var sonarrCache = cacheEntries
                .Where(c => c.AnimeSource == AnimeSource.Sonarr)
                .GroupBy(c => c.SourceId).ToDictionary(g => g.Key, g => g.Last().RawData);

            var mappingsByAniDbId = mappings
                .GroupBy(m => m.AniDbId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var animeDtos = new List<AnimeDetailsResponse>();
            foreach (var aniDbId in aniDbIds)
            {
                if (!mappingsByAniDbId.TryGetValue(aniDbId, out var group))
                {
                    logger.LogDebug(Constants.LogMessages.AnimeMediatorService.NoAnimeMappings, aniDbId);
                    continue;
                }

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
                    aniDbId,
                    group.FirstOrDefault()?.Anime?.Title ?? "NO_TITLE",
                    group.FirstOrDefault()?.Anime?.Description,
                    [.. plexAnimes],
                    [.. malAnimes],
                    [.. sonarrAnimes]
                ));
            }

            if (needsInMemoryPostFilter)
            {
                // Apply genre/status filter in memory (only over the pre-filtered subset)
                var filtered = ApplyFilters(animeDtos, filters, malCache, showNsfw);
                var sorted = ApplySorting(filtered, filters);
                totalCount = sorted.Count();
                var paged = sorted.Skip((page - 1) * pageSize).Take(pageSize);
                return Result.Success(PagedResponse<IEnumerable<AnimeDetailsResponse>>.Create(
                    paged, page, pageSize, totalCount));
            }

            // Fast path: already paginated at DB level; DB sort order is preserved by iterating aniDbIds
            return Result.Success(PagedResponse<IEnumerable<AnimeDetailsResponse>>.Create(
                animeDtos, page, pageSize, totalCount));
        }
        catch (Exception ex)
        {
            return Result.Failure<PagedResponse<IEnumerable<AnimeDetailsResponse>>>(new(Constants.Errors.GetAllAsync, ex.Message));
        }
    }

    private IEnumerable<AnimeDetailsResponse> ApplyFilters(
        IEnumerable<AnimeDetailsResponse> data,
        AnimeFilters? filters,
        Dictionary<string, string> malCache,
        bool showNsfw = true)
    {
        if (filters == null && showNsfw)
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
            var genreFilter = filters.Genre;
            result = result.Where(a =>
            {
                if (a.MalAnimes == null || !a.MalAnimes.Any())
                    return false;

                return a.MalAnimes.Any(m =>
                {
                    if (string.IsNullOrWhiteSpace(m.Id))
                        return false;

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
                        logger.LogWarning(Constants.LogMessages.AnimeMediatorService.MalDeserializeFailedGenreFilter, m.Id, ex.Message);
                        return false;
                    }
                });
            });
        }

        if (!showNsfw)
        {
            result = result.Where(a =>
            {
                if (a.MalAnimes == null || !a.MalAnimes.Any())
                    // No MAL data: cannot determine NSFW status; include by default
                    // (content may only exist in Sonarr/Plex without a MAL entry).
                    return true;

                // Exclude the anime if *any* of its MAL entries is explicitly NSFW.
                return !a.MalAnimes.Any(m =>
                {
                    if (string.IsNullOrWhiteSpace(m.Id))
                        return false; // no ID — cannot check; treat as safe

                    if (!malCache.TryGetValue(m.Id, out var malJson) || string.IsNullOrWhiteSpace(malJson))
                        return false; // no cache entry — treat as safe

                    try
                    {
                        var malAnime = JsonSerializer.Deserialize<MalAnime>(malJson, JsonOptions);
                        var nsfw = malAnime?.Nsfw;
                        // "gray" or "black" are the NSFW signals from the MAL API
                        return !string.IsNullOrEmpty(nsfw)
                            && !nsfw.Equals("white", StringComparison.OrdinalIgnoreCase);
                    }
                    catch (JsonException)
                    {
                        return false; // deserialisation error — treat as safe
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

    public async Task<Result<AnimeStatisticsResponse>> GetStatisticsAsync()
    {
        try
        {
            bool showNsfw = await IsNsfwAllowedAsync();

            // Execute all DB-backed queries sequentially — the scoped DbContext does not support concurrent operations.
            var allAnimesResult = await animeService.GetAllAsync();
            if (allAnimesResult.IsFailure)
                return Result.Failure<AnimeStatisticsResponse>(allAnimesResult.Error);

            var animes = allAnimesResult.Value.ToList();
            var plexCount = animes.Count(a => a.AnimeSource == AnimeSource.Plex);
            var malCount = animes.Count(a => a.AnimeSource == AnimeSource.MyAnimeList);
            var sonarrCount = animes.Count(a => a.AnimeSource == AnimeSource.Sonarr);

            var allAniDbIdsResult = await animeMappingService.GetAllFilteredAniDbIdsAsync();
            if (allAniDbIdsResult.IsFailure)
                return Result.Failure<AnimeStatisticsResponse>(allAniDbIdsResult.Error);

            var mappingsResult = await animeMappingService.GetByAniDbIdsAsync(allAniDbIdsResult.Value);
            if (mappingsResult.IsFailure)
                return Result.Failure<AnimeStatisticsResponse>(mappingsResult.Error);

            var mappingGroups = mappingsResult.Value
                .GroupBy(m => m.AniDbId)
                .Select(g =>
                {
                    var sources = g.Where(m => m.Anime != null)
                                   .Select(m => m.Anime!.AnimeSource)
                                   .Distinct()
                                   .ToHashSet();
                    return new
                    {
                        HasSonarr = sources.Contains(AnimeSource.Sonarr),
                        HasMal    = sources.Contains(AnimeSource.MyAnimeList),
                        HasPlex   = sources.Contains(AnimeSource.Plex),
                        Count     = sources.Count
                    };
                })
                .ToList();

            var matchedCount    = mappingGroups.Count(g => g.Count > 1);
            var sonarrNotInMal  = mappingGroups.Count(g => g.HasSonarr && !g.HasMal);
            var malNotInPlex    = mappingGroups.Count(g => g.HasMal    && !g.HasPlex);
            var plexNotInMal    = mappingGroups.Count(g => g.HasPlex   && !g.HasMal);
            var plexNotInSonarr = mappingGroups.Count(g => g.HasPlex   && !g.HasSonarr);

            var genreStats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var malEpisodesWatched = 0;
            var malTotalEpisodes = 0;

            var malCacheResult = await cacheEntryService.GetAsync(AnimeSource.MyAnimeList);
            if (malCacheResult.IsSuccess)
            {
                foreach (var entry in malCacheResult.Value)
                {
                    if (string.IsNullOrWhiteSpace(entry.RawData))
                        continue;

                    MalAnime? malAnime = null;
                    try
                    {
                        malAnime = JsonSerializer.Deserialize<MalAnime>(entry.RawData, JsonOptions);
                    }
                    catch (JsonException ex)
                    {
                        logger.LogWarning(ex, Constants.LogMessages.AnimeMediatorService.MalCacheDeserializeFailedStats, entry.SourceId);
                    }

                    if (malAnime == null)
                        continue;

                    // Skip NSFW entries when NSFW content is disabled
                    if (!showNsfw)
                    {
                        var nsfw = malAnime.Nsfw;
                        if (!string.IsNullOrEmpty(nsfw) && !nsfw.Equals("white", StringComparison.OrdinalIgnoreCase))
                            continue;
                    }

                    foreach (var genre in malAnime.Genres ?? [])
                    {
                        if (string.IsNullOrWhiteSpace(genre.Name))
                            continue;
                        genreStats.TryGetValue(genre.Name, out var existing);
                        genreStats[genre.Name] = existing + 1;
                    }

                    malEpisodesWatched += malAnime.MyListStatus?.NumEpisodesWatched ?? 0;
                    malTotalEpisodes += malAnime.NumEpisodes;
                }
            }

            var plexEpisodesWatched = 0;
            var plexTotalEpisodes = 0;

            var plexCacheResult = await cacheEntryService.GetAsync(AnimeSource.Plex);
            if (plexCacheResult.IsSuccess)
            {
                foreach (var entry in plexCacheResult.Value)
                {
                    if (string.IsNullOrWhiteSpace(entry.RawData))
                        continue;

                    PlexMedia? plexMedia = null;
                    try
                    {
                        plexMedia = JsonSerializer.Deserialize<PlexMedia>(entry.RawData, JsonOptions);
                    }
                    catch (JsonException ex)
                    {
                        logger.LogWarning(ex, Constants.LogMessages.AnimeMediatorService.PlexCacheDeserializeFailedStats, entry.SourceId);
                    }

                    if (plexMedia == null)
                        continue;

                    plexEpisodesWatched += plexMedia.ViewedLeafCount;
                    plexTotalEpisodes += plexMedia.LeafCount;
                }
            }

            return Result.Success(new AnimeStatisticsResponse(
                plexCount,
                malCount,
                sonarrCount,
                matchedCount,
                sonarrNotInMal,
                malNotInPlex,
                plexNotInMal,
                plexNotInSonarr,
                genreStats,
                new ViewingProgressStats(malEpisodesWatched, malTotalEpisodes, plexEpisodesWatched, plexTotalEpisodes)
            ));
        }
        catch (Exception ex)
        {
            return Result.Failure<AnimeStatisticsResponse>(new(Constants.Errors.GetStatisticsAsync, ex.Message));
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
            return Result.Failure(new(Constants.Errors.SyncAllAsync, ex.Message));
        }
    }

    public async Task<Result<AnimeDetailsResponse?>> GetByIdAsync(string aniDbId)
    {
        try
        {
            var mappingsResult = await animeMappingService.GetByAniDbIdsAsync([aniDbId]);
            if (mappingsResult.IsFailure)
            {
                return Result.Failure<AnimeDetailsResponse?>(mappingsResult.Error);
            }

            var animeMappings = mappingsResult.Value
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
                animeMappings.FirstOrDefault()?.Anime?.Description,
                [.. plexAnimes],
                [.. malAnimes],
                [.. sonarrAnimes]
            );

            return Result.Success<AnimeDetailsResponse?>(animeDto);
        }
        catch (Exception ex)
        {
            return Result.Failure<AnimeDetailsResponse?>(new(Constants.Errors.GetByIdAsync, ex.Message));
        }
    }

    public async Task<Result<IEnumerable<SonarrCodecSearchResponse>>> GetSonarrSeriesByCodecAsync(string codec)
    {
        try
        {
            var searchResult = await sonarrAnimeSource.GetSeriesByCodecAsync(codec);
            if (searchResult.IsFailure)
                return Result.Failure<IEnumerable<SonarrCodecSearchResponse>>(searchResult.Error);

            var responses = searchResult.Value.Select(r => new SonarrCodecSearchResponse(
                r.Id,
                r.Title,
                $"{sonarrSeriesBaseUrl}/series/{r.Id}",
                r.CodecDistribution,
                r.TotalSizeOnDisk
            ));

            return Result.Success(responses);
        }
        catch (Exception ex)
        {
            return Result.Failure<IEnumerable<SonarrCodecSearchResponse>>(new(Constants.Errors.GetSonarrSeriesByCodecAsync, ex.Message));
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
            return Result.Failure<AnimeResponse>(new(Constants.Errors.CreateAsync, ex.Message));
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
                return Result.Failure<AnimeResponse>(new(Constants.Errors.NotFound, "Anime not found"));
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
            return Result.Failure<AnimeResponse>(new(Constants.Errors.UpdateAsync, ex.Message));
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
                return Result.Failure(new(Constants.Errors.NotFound, "Anime not found"));
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
            return Result.Failure(new(Constants.Errors.DeleteAsync, ex.Message));
        }
    }

    #region Private Helper Methods

    /// <summary>
    /// Returns true when NSFW content should be shown (setting is "true"),
    /// false when it should be filtered out (setting is absent or "false").
    /// </summary>
    private async Task<bool> IsNsfwAllowedAsync()
    {
        var entry = await propertyBagRepository.GetByKeyAsync(MetaAnime.Core.SettingsKeys.AppShowNsfw);
        return entry?.Value?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;
    }

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
                        logger.LogDebug(Constants.LogMessages.AnimeMediatorService.MalAnimeMyListStatus,
                            mapping.Anime.SourceId, malAnime.Title, malAnime.MyListStatus?.Status);
                        return new MalAnimeInfo(malAnime, myAnimeListHost);
                    }
                }
                catch (JsonException ex)
                {
                    logger.LogWarning(ex, Constants.LogMessages.AnimeMediatorService.MalCacheDeserializeFailed, mapping.Anime.SourceId);
                }
            }
        }
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
                        logger.LogDebug(Constants.LogMessages.AnimeMediatorService.SonarrAnimeSeasons,
                            mapping.Anime.SourceId, sonarrSeries.Title, sonarrSeries.Seasons?.Count ?? 0);
                        return new SonarrAnimeInfo(sonarrSeries, sonarrSeriesBaseUrl, new Dictionary<string, int>());
                    }
                }
                catch (JsonException ex)
                {
                    logger.LogWarning(ex, Constants.LogMessages.AnimeMediatorService.SonarrCacheDeserializeFailed, mapping.Anime.SourceId);
                }
            }
        }
        return new SonarrAnimeInfo(mapping.Anime, sonarrSeriesBaseUrl);
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
