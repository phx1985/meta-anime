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
    IKometaAnimeMappingRepository kometaAnimeMappingRepository,
    IAnimeRepository animeRepository,
    IAnimeMappingRepository animeMappingRepository,
    IOptions<MalSettings> malOptions,
    IOptions<SonarrSettings> sonarrOptions,
    ILogger<AnimeMediatorService> logger)
    : IAnimeMediatorService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Genre names that are always treated as NSFW regardless of the MAL <c>nsfw</c> field value.</summary>
    private static readonly HashSet<string> NsfwGenres = new(StringComparer.OrdinalIgnoreCase)
    {
        "Ecchi", "Hentai", "Erotica"
    };

    private readonly string myAnimeListHost = malOptions.Value.WebBaseUrl;
    private readonly string sonarrSeriesBaseUrl = !string.IsNullOrWhiteSpace(sonarrOptions.Value.RemoteUrl)
        ? sonarrOptions.Value.RemoteUrl
        : sonarrOptions.Value.Host;

    // Lazily resolved the first time a URL is needed so the constructor stays synchronous.
    private string? _plexBaseUrl;
    private async Task<string> GetPlexBaseUrlAsync()
    {
        if (_plexBaseUrl is not null) return _plexBaseUrl;
        var idResult = await plexAnimeSource.GetMachineIdentifierAsync();
        _plexBaseUrl = $"https://app.plex.tv/desktop/#!/server/{idResult.Value}/details?key=%2Flibrary%2Fmetadata%2F";
        return _plexBaseUrl;
    }

    public async Task<Result<PagedResponse<IEnumerable<AnimeDetailsResponse>>>> GetAllAsync(int page = 1, int pageSize = 100, AnimeFilters? filters = null, bool enriched = false)
    {
        try
        {
            bool showNsfw = await IsNsfwAllowedAsync();
            // Similarity threshold is used only for the in-memory slow-path filter.
            double? similarityThreshold = !string.IsNullOrWhiteSpace(filters?.Title)
                ? await GetTitleSimilarityThresholdAsync()
                : null;

            AnimeSource? sourceFilter = filters?.Source?.ToLower() switch
            {
                "plex" => AnimeSource.Plex,
                "mal" => AnimeSource.MyAnimeList,
                "sonarr" => AnimeSource.Sonarr,
                _ => null
            };
            bool sortDescending = filters?.SortOrder?.Equals("desc", StringComparison.OrdinalIgnoreCase) == true;
            // Title is always handled in-memory (word-level Levenshtein fuzzy match).
            // The DB-level ILike pre-filter cannot handle typos, so we bypass it for title searches.
            bool needsInMemoryPostFilter = !string.IsNullOrWhiteSpace(filters?.Title)
                                         || filters?.Genre?.Length > 0
                                         || filters?.Theme?.Length > 0
                                         || filters?.Status?.Length > 0
                                         || filters?.AiringStatus?.Length > 0
                                         || filters?.HasFile.HasValue == true
                                         || filters?.Codec?.Length > 0
                                         || filters?.MediaType?.Length > 0
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
                // SLOWER PATH: genre/status/etc. require in-memory post-filtering.
                // Title is intentionally NOT passed to the DB: ILike cannot handle typos,
                // so we fetch all source-filtered IDs and let the word-level Levenshtein
                // in ApplyFilters perform typo-tolerant fuzzy matching in memory.
                var filteredResult = await animeMappingService.GetAllFilteredAniDbIdsAsync(
                    null, sourceFilter);
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
                    plexAnimes = (await Task.WhenAll(group
                        .Where(m => m.Anime?.AnimeSource == AnimeSource.Plex)
                        .Select(m => CreatePlexAnimeInfoFromCacheAsync(m, plexCache)))).ToList();
                    malAnimes = group
                        .Where(m => m.Anime?.AnimeSource == AnimeSource.MyAnimeList)
                        .Select(m => CreateMalAnimeInfoFromCache(m, malCache))
                        .ToList();
                    sonarrAnimes = group
                        .Where(m => m.Anime?.AnimeSource == AnimeSource.Sonarr)
                        .Select(m => CreateSonarrAnimeInfoFromCache(m, sonarrCache))
                        .ToList();
                }

                // Skip entries with no MAL association — they have no metadata to display.
                if (malAnimes.Count == 0)
                {
                    logger.LogDebug(Constants.LogMessages.AnimeMediatorService.NoAnimeMappings, aniDbId);
                    continue;
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
                // Apply all in-memory filters including fuzzy title similarity
                var filtered = ApplyFilters(animeDtos, filters, malCache, showNsfw, similarityThreshold);
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
        bool showNsfw = true,
        double? titleSimilarityThreshold = null)
    {
        if (filters == null && showNsfw)
            return data;

        var result = data;

        if (!string.IsNullOrWhiteSpace(filters.Title))
        {
            var query = filters.Title;
            var threshold = (titleSimilarityThreshold ?? 80) / 100.0;
            // Accept if the title contains the query as a substring OR
            // the word-level Levenshtein similarity meets the threshold (handles typos).
            result = result.Where(a =>
                a.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                ComputeWordLevelFuzzySimilarity(a.Title, query) >= threshold);
        }

        if (!string.IsNullOrWhiteSpace(filters.Source))
        {
            result = result.Where(a =>
                (filters.Source.Equals("plex", StringComparison.OrdinalIgnoreCase) && a.PlexAnimes?.Any() == true) ||
                (filters.Source.Equals("mal", StringComparison.OrdinalIgnoreCase) && a.MalAnimes?.Any() == true) ||
                (filters.Source.Equals("sonarr", StringComparison.OrdinalIgnoreCase) && a.SonarrAnimes?.Any() == true)
            );
        }

        if (filters.Status?.Length > 0)
        {
            result = result.Where(a =>
                filters.Status.Any(s =>
                    a.MalAnimes?.Any(m => m.ViewState != null &&
                        m.ViewState.Equals(s, StringComparison.OrdinalIgnoreCase)) == true));
        }

        if (filters.Genre?.Length > 0)
        {
            // Use a HashSet for O(1) genre lookups instead of O(n) inner Any()
            var genreSet = new HashSet<string>(filters.Genre, StringComparer.OrdinalIgnoreCase);
            bool isAnd = "and".Equals(filters.GenreOperator, StringComparison.OrdinalIgnoreCase);

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
                        var animeGenres = malAnime?.Genres?.Select(g => g.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                        if (animeGenres == null) return false;

                        // OR: any selected genre present; AND: all selected genres present
                        return isAnd
                            ? genreSet.All(g => animeGenres.Contains(g))
                            : genreSet.Any(g => animeGenres.Contains(g));
                    }
                    catch (JsonException ex)
                    {
                        logger.LogWarning(Constants.LogMessages.AnimeMediatorService.MalDeserializeFailedGenreFilter, m.Id, ex.Message);
                        return false;
                    }
                });
            });
        }

        if (filters?.Theme?.Length > 0)
        {
            // Theme filtering works identically to genre filtering — both are stored in the MAL Genres array.
            var themeSet = new HashSet<string>(filters.Theme, StringComparer.OrdinalIgnoreCase);
            bool isAnd = "and".Equals(filters.ThemeOperator, StringComparison.OrdinalIgnoreCase);

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
                        var animeGenres = malAnime?.Genres?.Select(g => g.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                        if (animeGenres == null) return false;

                        return isAnd
                            ? themeSet.All(t => animeGenres.Contains(t))
                            : themeSet.Any(t => animeGenres.Contains(t));
                    }
                    catch (JsonException ex)
                    {
                        logger.LogWarning(Constants.LogMessages.AnimeMediatorService.MalDeserializeFailedGenreFilter, m.Id, ex.Message);
                        return false;
                    }
                });
            });
        }

        // AiringStatus maps to the MAL series broadcast status (finished_airing / currently_airing / not_yet_aired)
        if (filters?.AiringStatus?.Length > 0)
        {
            result = result.Where(a =>
                filters.AiringStatus.Any(s =>
                    a.MalAnimes?.Any(m => m.Status != null &&
                        m.Status.Contains(s, StringComparison.OrdinalIgnoreCase)) == true));
        }

        if (filters?.HasFile.HasValue == true)
        {
            var wantFiles = filters.HasFile.Value;
            result = result.Where(a => HasDownloadedFiles(a) == wantFiles);
        }

        if (filters?.Codec?.Length > 0)
        {
            result = result.Where(a =>
                a.SonarrAnimes?.Any(s =>
                    s.CodecDistribution?.Keys.Any(k =>
                        filters.Codec.Any(c => k.Contains(c, StringComparison.OrdinalIgnoreCase))) == true) == true);
        }

        if (filters?.MediaType?.Length > 0)
        {
            result = result.Where(a =>
                filters.MediaType.Any(t =>
                    a.MalAnimes?.Any(m => m.MediaType != null &&
                        m.MediaType.Equals(t, StringComparison.OrdinalIgnoreCase)) == true));
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
                        return malAnime != null && IsNsfwAnime(malAnime);
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

        var desc = filters.SortOrder?.Equals("desc", StringComparison.OrdinalIgnoreCase) == true;

        return filters.SortBy.ToLower() switch
        {
            "title"         => desc ? data.OrderByDescending(a => a.Title)   : data.OrderBy(a => a.Title),
            "anidbid"       => desc ? data.OrderByDescending(a => a.AniDbId) : data.OrderBy(a => a.AniDbId),
            "malscore"      => desc ? data.OrderByDescending(a => a.MalAnimes?.FirstOrDefault()?.Rating?.MalMean  ?? 0)
                                    : data.OrderBy(a => a.MalAnimes?.FirstOrDefault()?.Rating?.MalMean  ?? 0),
            "myscore"       => desc ? data.OrderByDescending(a => a.MalAnimes?.FirstOrDefault()?.Rating?.MyScore  ?? 0)
                                    : data.OrderBy(a => a.MalAnimes?.FirstOrDefault()?.Rating?.MyScore  ?? 0),
            "startdate"     => desc ? data.OrderByDescending(a => a.MalAnimes?.FirstOrDefault()?.StartDate)
                                    : data.OrderBy(a => a.MalAnimes?.FirstOrDefault()?.StartDate),
            "enddate"       => desc ? data.OrderByDescending(a => a.MalAnimes?.FirstOrDefault()?.EndDate)
                                    : data.OrderBy(a => a.MalAnimes?.FirstOrDefault()?.EndDate),
            "addeddate"     => desc ? data.OrderByDescending(a => a.MalAnimes?.FirstOrDefault()?.AddedDate)
                                    : data.OrderBy(a => a.MalAnimes?.FirstOrDefault()?.AddedDate),
            "sizeondisk"    => desc ? data.OrderByDescending(a => a.SonarrAnimes?.FirstOrDefault()?.SizeOnDisk ?? 0)
                                    : data.OrderBy(a => a.SonarrAnimes?.FirstOrDefault()?.SizeOnDisk ?? 0),
            "totalsizedisk" => desc ? data.OrderByDescending(a => a.SonarrAnimes?.Sum(s => s.SizeOnDisk ?? 0) ?? 0)
                                    : data.OrderBy(a => a.SonarrAnimes?.Sum(s => s.SizeOnDisk ?? 0) ?? 0),
            _               => data.OrderBy(a => a.Title)
        };
    }

    /// <summary>
    /// Computes a word-level fuzzy similarity between a search query and an anime title.
    /// For each word in the query, finds the best-matching word in the title using normalised
    /// Levenshtein distance, then averages those per-word scores.
    /// This correctly handles single-character typos (e.g. "dargon ball" ≈ "Dragon Ball Z").
    /// Returns a value in [0.0, 1.0].
    /// </summary>
    private static double ComputeWordLevelFuzzySimilarity(string title, string query)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(query)) return 0.0;

        var qWords = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var tWords = title.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (qWords.Length == 0 || tWords.Length == 0) return 0.0;

        double totalScore = 0;
        foreach (var qWord in qWords)
        {
            double bestWordScore = 0;
            foreach (var tWord in tWords)
            {
                double score = NormalisedLevenshteinSimilarity(qWord, tWord);
                if (score > bestWordScore) bestWordScore = score;
                if (bestWordScore >= 1.0) break; // exact match found – no need to check further
            }
            totalScore += bestWordScore;
        }

        return totalScore / qWords.Length;
    }

    /// <summary>
    /// Returns 1 − (Levenshtein(a, b) / max(|a|, |b|)), i.e. 1.0 for identical strings.
    /// </summary>
    private static double NormalisedLevenshteinSimilarity(string a, string b)
    {
        if (a == b) return 1.0;
        int maxLen = Math.Max(a.Length, b.Length);
        if (maxLen == 0) return 1.0;
        // Skip if lengths differ too much to ever reach the caller's 80% threshold
        int minLen = Math.Min(a.Length, b.Length);
        if ((double)minLen / maxLen < 0.2) return 0.0;
        return 1.0 - (double)LevenshteinDistance(a, b) / maxLen;
    }

    /// <summary>Standard iterative Levenshtein (edit) distance.</summary>
    private static int LevenshteinDistance(string s, string t)
    {
        int m = s.Length, n = t.Length;
        if (m == 0) return n;
        if (n == 0) return m;

        var prev = new int[n + 1];
        var curr = new int[n + 1];

        for (int j = 0; j <= n; j++) prev[j] = j;

        for (int i = 1; i <= m; i++)
        {
            curr[0] = i;
            for (int j = 1; j <= n; j++)
            {
                int cost = s[i - 1] == t[j - 1] ? 0 : 1;
                curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            Array.Copy(curr, prev, n + 1);
        }

        return prev[n];
    }

    /// <summary>
    /// Returns <c>true</c> when the anime has at least one Sonarr series with downloaded episode files
    /// (determined by non-zero SizeOnDisk or at least one season with available episodes).
    /// </summary>
    private static bool HasDownloadedFiles(AnimeDetailsResponse anime) =>
        anime.SonarrAnimes?.Any(s =>
            (s.SizeOnDisk != null && s.SizeOnDisk > 0) ||
            s.SeasonInfo?.Any(si => si.AvailableEpisodes > 0) == true) == true;

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
                    if (!showNsfw && IsNsfwAnime(malAnime))
                        continue;

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

    public async Task<Result<IEnumerable<SeasonalAnimeResponse>>> GetSeasonalAnimesAsync(int year, string season)
    {
        try
        {
            bool showNsfw = await IsNsfwAllowedAsync();
            var seasonalResult = await malAnimeSource.GetSeasonalAsync(year, season);
            if (seasonalResult.IsFailure)
                return Result.Failure<IEnumerable<SeasonalAnimeResponse>>(seasonalResult.Error);

            var items = seasonalResult.Value;
            var seasonalMalIds = items.Select(a => a.Id.ToString()).ToList();

            // ── Kometa lookup: seasonal malIds → aniDbId + tvdbId ────────────────────────
            // Single self-join query treats the Kometa mapping as the sole source of truth.
            // TvdbId drives the exact `tvdb:XXXXX` Sonarr lookup; AniDbId drives provider state.
            var malToAniDb  = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var aniDbToTvdb = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (seasonalMalIds.Count > 0)
            {
                var kometaMappings = await kometaAnimeMappingRepository.GetMalMappingsWithTvdbAsync(seasonalMalIds);
                foreach (var (malId, aniDbId, tvdbId) in kometaMappings)
                {
                    malToAniDb[malId] = aniDbId;
                    if (tvdbId != null)
                        aniDbToTvdb[aniDbId] = tvdbId;
                }
            }

            // ── Build aniDbId → sources from AnimeMapping ──────────────────────────────
            // Includes both full-sync entries and mini-sync entries written by
            // AddSonarrSeriesAsync / UpdateMalListStatusAsync, so no property bag needed.
            var aniDbToSources = new Dictionary<string, (bool HasPlex, string? PlexId, bool HasSonarr, string? SonarrId, bool HasMal, string? MalId)>(StringComparer.OrdinalIgnoreCase);
            bool anyInPlex = false;
            var mappingsResult = await animeMappingService.GetAllAsync(includeAnime: true);
            if (mappingsResult.IsSuccess)
            {
                foreach (var aniDbGroup in mappingsResult.Value.Where(m => m.Anime != null).GroupBy(m => m.AniDbId))
                {
                    var animes      = aniDbGroup.Select(m => m.Anime!).ToList();
                    var plexEntry   = animes.FirstOrDefault(a => a.AnimeSource == AnimeSource.Plex);
                    var sonarrEntry = animes.FirstOrDefault(a => a.AnimeSource == AnimeSource.Sonarr);
                    var malEntry    = animes.FirstOrDefault(a => a.AnimeSource == AnimeSource.MyAnimeList);
                    bool hasPlex    = plexEntry != null;
                    if (hasPlex) anyInPlex = true;
                    aniDbToSources[aniDbGroup.Key] = (hasPlex, plexEntry?.SourceId, sonarrEntry != null, sonarrEntry?.SourceId, malEntry != null, malEntry?.SourceId);
                }
            }

            // Resolve Plex base URL only if needed
            string? plexBaseUrl = anyInPlex ? await GetPlexBaseUrlAsync() : null;

            // ── Sonarr TitleSlug lookup (for building correct Sonarr web UI URLs) ───────
            var sonarrIds = aniDbToSources.Values
                .Where(s => s.HasSonarr && s.SonarrId != null)
                .Select(s => s.SonarrId!)
                .Distinct()
                .ToList();
            var sonarrSlugById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (sonarrIds.Count > 0)
            {
                var cacheResult = await cacheEntryService.GetBySourceIdsAsync(sonarrIds);
                if (cacheResult.IsSuccess)
                {
                    foreach (var entry in cacheResult.Value.Where(e => e.AnimeSource == AnimeSource.Sonarr))
                    {
                        try
                        {
                            var series = JsonSerializer.Deserialize<SonarrSeries>(entry.RawData, JsonOptions);
                            if (series?.TitleSlug != null)
                                sonarrSlugById[entry.SourceId] = series.TitleSlug;
                        }
                        catch (JsonException ex)
                        {
                            logger.LogWarning(ex, Constants.LogMessages.AnimeMediatorService.SonarrCacheDeserializeFailed, entry.SourceId);
                        }
                    }
                }
            }

            // ── MAL view-state lookup (watching / completed / on_hold / dropped / plan_to_watch) ──
            var malIdsInLibrary = aniDbToSources.Values
                .Where(s => s.HasMal && s.MalId != null)
                .Select(s => s.MalId!)
                .Distinct()
                .ToList();
            var malViewStateById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (malIdsInLibrary.Count > 0)
            {
                var malCacheResult = await cacheEntryService.GetBySourceIdsAsync(malIdsInLibrary);
                if (malCacheResult.IsSuccess)
                {
                    foreach (var entry in malCacheResult.Value.Where(e => e.AnimeSource == AnimeSource.MyAnimeList))
                    {
                        try
                        {
                            var malAnime = JsonSerializer.Deserialize<MalAnime>(entry.RawData, JsonOptions);
                            if (malAnime?.MyListStatus?.Status != null)
                                malViewStateById[entry.SourceId] = malAnime.MyListStatus.Status;
                        }
                        catch (JsonException ex)
                        {
                            logger.LogWarning(ex, Constants.LogMessages.AnimeMediatorService.MalCacheDeserializeFailed, entry.SourceId);
                        }
                    }
                }
            }

            // ── NSFW filtering ──────────────────────────────────────────────────────────
            var filteredItems = showNsfw ? items : items.Where(anime => !IsNsfwAnime(anime)).ToList();

            var responses = filteredItems.Select(anime =>
            {
                var malId = anime.Id.ToString();
                malToAniDb.TryGetValue(malId, out var aniDbId);
                var sources  = aniDbId != null && aniDbToSources.TryGetValue(aniDbId, out var s) ? s : default;
                var inMal    = sources.HasMal;
                var inSonarr = sources.HasSonarr;
                var inPlex   = sources.HasPlex;
                // plexBaseUrl ends with %2F (URL-encoded "/"), so appending source ID
                // directly produces the correct path: .../metadata/{ratingKey}
                string? plexUrl = inPlex && plexBaseUrl != null && sources.PlexId != null
                    ? $"{plexBaseUrl}{sources.PlexId}" : null;
                // Use TitleSlug from cache (same as regular anime card); fall back to numeric ID
                var sonarrSlug = sources.SonarrId != null && sonarrSlugById.TryGetValue(sources.SonarrId, out var slug)
                    ? slug : sources.SonarrId;
                string? sonarrUrl = inSonarr && sonarrSlug != null
                    ? $"{sonarrSeriesBaseUrl.TrimEnd('/')}/series/{sonarrSlug}" : null;
                var tvdbId = aniDbId != null && aniDbToTvdb.TryGetValue(aniDbId, out var tId) ? tId : null;
                string? malViewState = sources.MalId != null && malViewStateById.TryGetValue(sources.MalId, out var vs) ? vs : null;
                return new SeasonalAnimeResponse(
                    MalId: malId,
                    Title: anime.Title,
                    Synopsis: anime.Synopsis,
                    Genres: anime.Genres?.Select(g => g.Name).ToArray(),
                    CoverImageUrl: anime.MainPicture?.Large ?? anime.MainPicture?.Medium,
                    MalScore: anime.Mean,
                    StartDate: anime.StartDate,
                    MediaType: anime.MediaType,
                    Status: anime.Status,
                    MalUrl: $"{myAnimeListHost}/anime/{malId}",
                    InMal: inMal,
                    InPlex: inPlex,
                    PlexUrl: plexUrl,
                    InSonarr: inSonarr,
                    SonarrUrl: sonarrUrl,
                    TvdbId: tvdbId,
                    EnglishTitle: anime.AlternativeTitles?.En,
                    MalViewState: malViewState
                );
            });

            return Result.Success(responses.AsEnumerable());
        }
        catch (Exception ex)
        {
            return Result.Failure<IEnumerable<SeasonalAnimeResponse>>(new(Constants.Errors.GetAllAsync, ex.Message));
        }
    }

    public async Task<Result<SonarrOptionsResponse>> GetSonarrOptionsAsync()
    {
        try
        {
            var optionsResult = await sonarrAnimeSource.GetOptionsAsync();
            if (optionsResult.IsFailure)
                return Result.Failure<SonarrOptionsResponse>(optionsResult.Error);

            var opts = optionsResult.Value;
            return Result.Success(new SonarrOptionsResponse(
                opts.RootFolders.Select(f => new SonarrRootFolderDto(f.Id, f.Path, f.FreeSpace)).ToList(),
                opts.QualityProfiles.Select(p => new SonarrQualityProfileDto(p.Id, p.Name)).ToList(),
                opts.AnimeTagIds
            ));
        }
        catch (Exception ex)
        {
            return Result.Failure<SonarrOptionsResponse>(new(Constants.Errors.GetAllAsync, ex.Message));
        }
    }

    public async Task<Result<IList<SonarrLookupItem>>> SearchSonarrLookupAsync(string term)
    {
        try
        {
            return await sonarrAnimeSource.SearchLookupAsync(term);
        }
        catch (Exception ex)
        {
            return Result.Failure<IList<SonarrLookupItem>>(new(Constants.Errors.GetAllAsync, ex.Message));
        }
    }

    public async Task<Result<SonarrAddResult>> AddSonarrSeriesAsync(int tvdbId, string rootFolderPath, int qualityProfileId, string? malId = null)
    {
        try
        {
            // Get the configured tag IDs to apply to the new series
            var optionsResult = await sonarrAnimeSource.GetOptionsAsync();
            var tagIds = optionsResult.IsSuccess ? optionsResult.Value.AnimeTagIds : new List<int>();

            var addResult = await sonarrAnimeSource.AddSeriesAsync(tvdbId, rootFolderPath, qualityProfileId, tagIds);
            if (addResult.IsFailure)
                return Result.Failure<SonarrAddResult>(addResult.Error);

            var added = addResult.Value;

            // Construct a SonarrSeries for cache/DB persistence (full JSON including TvdbId)
            var seriesForSync = new SonarrSeries { Id = added.Id, Title = added.Title, TitleSlug = added.TitleSlug, TvdbId = tvdbId };

            // Write full cache entry for immediate TitleSlug availability
            await cacheEntryService.AddAsync(Result.Success(seriesForSync.ToCacheEntry()));

            // Mini-sync: write Anime + AnimeMapping so GetSeasonalAnimesAsync reflects the
            // add immediately without waiting for a full sync.
            var existingAnime = await animeRepository.GetAnimeAsync(AnimeSource.Sonarr, added.Id.ToString());
            if (existingAnime == null)
            {
                // Look up AniDbId via Kometa (TvdbId → AniDbId)
                string? aniDbId = null;
                if (tvdbId != 0)
                {
                    var tvdbMappings = await kometaAnimeMappingRepository.GetByPlatformIdsAsync(
                        MetaAnime.Domain.Enums.KometaAnimePlatform.Tvdb, new[] { tvdbId.ToString() });
                    aniDbId = tvdbMappings.FirstOrDefault()?.AniDbId;
                }

                var animeForDb = seriesForSync.ToAnime();
                await animeRepository.AddAsync(animeForDb);

                if (aniDbId != null)
                    await animeMappingRepository.AddAsync(new AnimeMapping { AniDbId = aniDbId, AnimeId = animeForDb.Id });
            }

            var url = !string.IsNullOrEmpty(added.TitleSlug)
                ? $"{sonarrSeriesBaseUrl.TrimEnd('/')}/series/{added.TitleSlug}"
                : null;
            return Result.Success(new SonarrAddResult(added.Id, added.Title, added.TitleSlug, url));
        }
        catch (Exception ex)
        {
            return Result.Failure<SonarrAddResult>(new(Constants.Errors.GetAllAsync, ex.Message));
        }
    }

    public async Task<Result> RemoveSonarrSeriesAsync(int sonarrId)
    {
        try
        {
            var result = await sonarrAnimeSource.RemoveSeriesAsync(sonarrId);
            if (result.IsSuccess)
            {
                await cacheEntryService.RemoveAsync(sonarrId.ToString(), AnimeSource.Sonarr);
                // Remove any mini-sync Anime record; FK cascade deletes AnimeMapping + ExternalIdentifiers
                var anime = await animeRepository.GetAnimeAsync(AnimeSource.Sonarr, sonarrId.ToString());
                if (anime != null)
                    await animeRepository.DeleteAsync(anime.Id);
            }
            return result;
        }
        catch (Exception ex)
        {
            return Result.Failure(new(Constants.Errors.GetAllAsync, ex.Message));
        }
    }

    public async Task<Result> UpdateMalListStatusAsync(string malId, string status, int numWatchedEpisodes, int score)
    {
        try
        {
            var result = await malAnimeSource.UpdateListStatusAsync(malId, status, numWatchedEpisodes, score);
            if (result.IsFailure)
                return result;

            // Mini-sync: fetch full anime details and write CacheEntry + Anime + AnimeMapping
            // so GetSeasonalAnimesAsync reflects the MAL add immediately.
            var malAnimeResult = await malAnimeSource.GetByIdAsync(malId);
            if (malAnimeResult.IsSuccess && malAnimeResult.Value != null)
            {
                var malAnime = malAnimeResult.Value;

                // Replace any stale cache entry with the fresh fetch
                var existingCache = await cacheEntryService.GetAsync(AnimeSource.MyAnimeList, malId);
                if (existingCache.IsSuccess && existingCache.Value != null)
                    await cacheEntryService.RemoveAsync(malId, AnimeSource.MyAnimeList);
                await cacheEntryService.AddAsync(Result.Success(malAnime.ToCacheEntry()));

                // Write Anime + AnimeMapping if not already present
                var existingAnime = await animeRepository.GetAnimeAsync(AnimeSource.MyAnimeList, malId);
                if (existingAnime == null)
                {
                    // Look up AniDbId via Kometa (malId → AniDbId)
                    var malMappings = await kometaAnimeMappingRepository.GetByPlatformIdsAsync(
                        MetaAnime.Domain.Enums.KometaAnimePlatform.Mal, new[] { malId });
                    var aniDbId = malMappings.FirstOrDefault()?.AniDbId;

                    var animeForDb = malAnime.ToAnime();
                    await animeRepository.AddAsync(animeForDb);

                    if (aniDbId != null)
                        await animeMappingRepository.AddAsync(new AnimeMapping { AniDbId = aniDbId, AnimeId = animeForDb.Id });
                }
            }
            else
            {
                logger.LogWarning("Failed to fetch MAL anime details for mini-sync after adding MAL ID {MalId}: {Error}",
                    malId, malAnimeResult.IsFailure ? malAnimeResult.Error.Message : "null response");
            }

            return result;
        }
        catch (Exception ex)
        {
            return Result.Failure(new(Constants.Errors.GetAllAsync, ex.Message));
        }
    }

    #region Private Helper Methods
    private async Task<bool> IsNsfwAllowedAsync()
    {
        var entry = await propertyBagRepository.GetByKeyAsync(MetaAnime.Core.SettingsKeys.AppShowNsfw);
        return entry?.Value?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;
    }

    /// <summary>
    /// Returns the title similarity threshold (0–100) stored in settings.
    /// Falls back to 80 if the setting is absent or invalid.
    /// </summary>
    private async Task<double> GetTitleSimilarityThresholdAsync()
    {
        var entry = await propertyBagRepository.GetByKeyAsync(MetaAnime.Core.SettingsKeys.AppTitleSimilarityThreshold);
        if (double.TryParse(entry?.Value, out var v) && v is >= 1 and <= 100)
            return v;
        return 80; // default
    }

    /// <summary>
    /// Returns true when the MAL anime should be considered NSFW and filtered out.
    /// Checks the MAL <c>nsfw</c> field AND explicit NSFW genres (Ecchi, Hentai, Erotica).
    /// </summary>
    private static bool IsNsfwAnime(MalAnime malAnime)
    {
        // MAL API nsfw field: "gray" or "black" = NSFW, "white" = SFW
        var nsfw = malAnime.Nsfw;
        if (!string.IsNullOrEmpty(nsfw) && !nsfw.Equals("white", StringComparison.OrdinalIgnoreCase))
            return true;

        // Some Ecchi/Hentai titles are rated "white" by MAL but carry explicit genres
        return malAnime.Genres?.Any(g => NsfwGenres.Contains(g.Name)) == true;
    }

    private async Task<PlexAnimeInfo> CreatePlexAnimeInfoFromCacheAsync(AnimeMapping mapping, Dictionary<string, string> plexCache)
    {
        var baseUrl = await GetPlexBaseUrlAsync();
        if (plexCache.TryGetValue(mapping.Anime!.SourceId, out var rawData))
        {
            try
            {
                var plexMedia = JsonSerializer.Deserialize<PlexMedia>(rawData, JsonOptions);
                if (plexMedia != null)
                {
                    return new PlexAnimeInfo(
                        plexMedia,
                        baseUrl,
                        plexMedia.ViewedLeafCount,
                        plexMedia.LeafCount
                    );
                }
            }
            catch (JsonException)
            {
            }
        }
        return new PlexAnimeInfo(mapping.Anime, baseUrl);
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
                        return new SonarrAnimeInfo(sonarrSeries, sonarrSeriesBaseUrl, sonarrSeries.CodecDistribution);
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
        var baseUrl = await GetPlexBaseUrlAsync();
        var tasks = mappings.Select(async mapping =>
        {
            var plexResult = await plexAnimeSource.GetByIdAsync(mapping.Anime!.SourceId);
            if (plexResult.IsSuccess && plexResult.Value != null)
            {
                return new PlexAnimeInfo(
                    plexResult.Value,
                    baseUrl,
                    plexResult.Value.ViewedLeafCount,
                    plexResult.Value.LeafCount
                );
            }
            else
            {
                return new PlexAnimeInfo(mapping.Anime, baseUrl);
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
