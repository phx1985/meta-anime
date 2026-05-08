using MetaAnime.Application.Contracts;
using MetaAnime.Application.Interfaces;
using MetaAnime.Application.Queries;
using MetaAnime.Domain.Enums;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Interfaces.Shared.Results;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Domain.Entities;
using MetaAnime.Core;
using MetaAnime.Core.Settings;
using MetaAnime.Infrastructure.MyAnimeList.Models;
using MetaAnime.Infrastructure.Plex.Models;
using MetaAnime.Infrastructure.SonarrAPIs.Sonarr.Models;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using MalSearchResultItem = MetaAnime.Core.Models.MalSearchResultItem;

namespace MetaAnime.Application.Services;

public sealed class AnimeMediatorService(
    IAnimeMappingService animeMappingService,
    IPlexAnimeSourceService<PlexMedia> plexAnimeSource,
    IMalAnimeSourceService<MalAnime> malAnimeSource,
    ISonarrAnimeSourceService<SonarrSeries> sonarrAnimeSource,
    IAnimeMatchingService animeMatchingService,
    IAnimeService animeService,
    ICacheEntryService cacheEntryService,
    ICrossLinkService crossLinkService,
    IPropertyBagRepository propertyBagRepository,
    IKometaAnimeMappingRepository kometaAnimeMappingRepository,
    IAnimeRepository animeRepository,
    IAnimeMappingRepository animeMappingRepository,
    IOptions<MalSettings> malOptions,
    ILogger<AnimeMediatorService> logger)
    : IAnimeMediatorService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    private const int    TitleSimilarityDefault = 80;
    private const double MinLengthRatioForFuzzy = 0.2;
    private const string SonarrCoverType        = "poster";

    /// <summary>Maps filter codec tokens to all equivalent codec strings that may appear in file metadata.</summary>
    private static readonly Dictionary<string, string[]> CodecAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["h264"] = ["h264", "x264", "avc"],
        ["hevc"] = ["hevc", "h265", "x265"],
        ["av1"] = ["av1"],
        ["mpeg2"] = ["mpeg2", "mpeg-2"],
    };

    /// <summary>Returns all alias strings for a codec filter token (falls back to the token itself).</summary>
    private static IEnumerable<string> GetCodecAliases(string codec) =>
        CodecAliases.TryGetValue(codec, out var aliases) ? aliases : [codec];

    private readonly string myAnimeListHost = malOptions.Value.WebBaseUrl;

    // Lazily resolved the first time a URL is needed so the constructor stays synchronous.
    private string? _plexBaseUrl;
    private async Task<string> GetPlexBaseUrlAsync()
    {
        if (_plexBaseUrl is not null) return _plexBaseUrl;
        var idResult = await plexAnimeSource.GetMachineIdentifierAsync();
        if (!idResult.IsSuccess || string.IsNullOrWhiteSpace(idResult.Value))
            return _plexBaseUrl = string.Empty;
        _plexBaseUrl = $"https://app.plex.tv/desktop/#!/server/{idResult.Value}/details?key=%2Flibrary%2Fmetadata%2F";
        return _plexBaseUrl;
    }

    // Reads the Sonarr base URL from the PropertyBag at request time so that
    // settings configured via the UI (not appsettings) are always reflected.
    // Cached for the duration of a single request only (service is Scoped).
    private string? _sonarrSeriesBaseUrl;
    private async Task<string> GetSonarrSeriesBaseUrlAsync()
    {
        if (_sonarrSeriesBaseUrl is not null) return _sonarrSeriesBaseUrl;
        var remoteUrlEntry = await propertyBagRepository.GetByKeyAsync(SettingsKeys.SonarrRemoteUrl);
        var remoteUrl = remoteUrlEntry?.Value;
        if (!string.IsNullOrWhiteSpace(remoteUrl))
            return _sonarrSeriesBaseUrl = remoteUrl;
        var hostEntry = await propertyBagRepository.GetByKeyAsync(SettingsKeys.SonarrHost);
        return _sonarrSeriesBaseUrl = hostEntry?.Value ?? string.Empty;
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
                FilterConstants.Source.Plex   => AnimeSource.Plex,
                FilterConstants.Source.Mal    => AnimeSource.MyAnimeList,
                FilterConstants.Source.Sonarr => AnimeSource.Sonarr,
                _ => null
            };
            bool sortDescending = filters?.SortOrder?.Equals(FilterConstants.SortOrder.Descending, StringComparison.OrdinalIgnoreCase) == true;
            // The DB repository only supports title/anidbid sorting. Any other sort field (malscore,
            // sizeondisk, avgepsize, etc.) requires in-memory ApplySorting() on the full result set.
            bool sortRequiresInMemory = !string.IsNullOrWhiteSpace(filters?.SortBy) &&
                filters.SortBy.ToLower() is not (FilterConstants.SortField.Title or FilterConstants.SortField.AniDbId);
            // Title is always handled in-memory (word-level Levenshtein fuzzy match).
            // The DB-level ILike pre-filter cannot handle typos, so we bypass it for title searches.
            bool needsInMemoryPostFilter = sortRequiresInMemory
                                         || !string.IsNullOrWhiteSpace(filters?.Title)
                                         || filters?.Genre?.Length > 0
                                         || filters?.Theme?.Length > 0
                                         || filters?.Demographic?.Length > 0
                                         || filters?.Status?.Length > 0
                                         || filters?.AiringStatus?.Length > 0
                                         || filters?.HasFile.HasValue == true
                                         || filters?.Codec?.Length > 0
                                         || filters?.MediaType?.Length > 0
                                         || filters?.AgeRating?.Length > 0
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

            var sonarrBaseUrl = await GetSonarrSeriesBaseUrlAsync();

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
                        .Select(m => CreateSonarrAnimeInfoFromCache(m, sonarrCache, sonarrBaseUrl))
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
                var filtered    = ApplyFilters(animeDtos, filters, malCache, showNsfw, similarityThreshold);
                var sortedList  = ApplySorting(filtered, filters).ToList();
                totalCount      = sortedList.Count;
                var paged       = sortedList.Skip((page - 1) * pageSize).Take(pageSize);
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

        if (!string.IsNullOrWhiteSpace(filters?.Title))
        {
            var query = filters.Title;
            var threshold = (titleSimilarityThreshold ?? TitleSimilarityDefault) / 100.0;
            // Accept if the title contains the query as a substring OR
            // the word-level Levenshtein similarity meets the threshold (handles typos).
            // Also search in alternative titles (English, Japanese, synonyms) from MAL.
            result = result.Where(a =>
                TitleMatches(a.Title, query, threshold) ||
                a.MalAnimes?.Any(m =>
                    TitleMatches(m.AlternativeTitles?.En, query, threshold) ||
                    TitleMatches(m.AlternativeTitles?.Ja, query, threshold) ||
                    m.AlternativeTitles?.Synonyms?.Any(s => TitleMatches(s, query, threshold)) == true
                ) == true);
        }

        if (!string.IsNullOrWhiteSpace(filters?.Source))
        {
            var source = filters.Source;
            result = result.Where(a =>
                (source.Equals(FilterConstants.Source.Plex,   StringComparison.OrdinalIgnoreCase) && a.PlexAnimes?.Any() == true) ||
                (source.Equals(FilterConstants.Source.Mal,    StringComparison.OrdinalIgnoreCase) && a.MalAnimes?.Any() == true) ||
                (source.Equals(FilterConstants.Source.Sonarr, StringComparison.OrdinalIgnoreCase) && a.SonarrAnimes?.Any() == true)
            );
        }

        if (filters?.Status?.Length > 0)
        {
            result = result.Where(a =>
                filters.Status.Any(s =>
                    a.MalAnimes?.Any(m => m.ViewState != null &&
                        m.ViewState.Equals(s, StringComparison.OrdinalIgnoreCase)) == true));
        }

        result = ApplyTagsFilter(result, filters?.Genre,        filters?.GenreOperator,       malCache);
        result = ApplyTagsFilter(result, filters?.Theme,        filters?.ThemeOperator,       malCache);
        result = ApplyTagsFilter(result, filters?.Demographic,  filters?.DemographicOperator, malCache);

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
                        filters.Codec.Any(c => GetCodecAliases(c)
                            .Any(alias => k.Contains(alias, StringComparison.OrdinalIgnoreCase)))) == true) == true);
        }

        if (filters?.MediaType?.Length > 0)
        {
            result = result.Where(a =>
                filters.MediaType.Any(t =>
                    a.MalAnimes?.Any(m => m.MediaType != null &&
                        m.MediaType.Equals(t, StringComparison.OrdinalIgnoreCase)) == true));
        }

        if (filters?.AgeRating?.Length > 0)
        {
            result = result.Where(a =>
                a.MalAnimes?.Any(m =>
                    m.AgeRating != null &&
                    filters.AgeRating.Any(r =>
                        m.AgeRating.Equals(r, StringComparison.OrdinalIgnoreCase))) == true);
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

        var desc = filters.SortOrder?.Equals(FilterConstants.SortOrder.Descending, StringComparison.OrdinalIgnoreCase) == true;

        return filters.SortBy.ToLower() switch
        {
            FilterConstants.SortField.Title   => desc ? data.OrderByDescending(a => a.Title) : data.OrderBy(a => a.Title),
            FilterConstants.SortField.AniDbId => desc ? data.OrderByDescending(a => a.AniDbId) : data.OrderBy(a => a.AniDbId),
            FilterConstants.SortField.MalScore => desc ? data.OrderByDescending(a => a.MalAnimes?.FirstOrDefault()?.Rating?.MalMean ?? 0)
                                : data.OrderBy(a => a.MalAnimes?.FirstOrDefault()?.Rating?.MalMean ?? 0),
            FilterConstants.SortField.MyScore => desc ? data.OrderByDescending(a => a.MalAnimes?.FirstOrDefault()?.Rating?.MyScore ?? 0)
                               : data.OrderBy(a => a.MalAnimes?.FirstOrDefault()?.Rating?.MyScore ?? 0),
            "startdate" => desc ? data.OrderByDescending(a => a.MalAnimes?.FirstOrDefault()?.StartDate)
                                 : data.OrderBy(a => a.MalAnimes?.FirstOrDefault()?.StartDate),
            "enddate" => desc ? data.OrderByDescending(a => a.MalAnimes?.FirstOrDefault()?.EndDate)
                               : data.OrderBy(a => a.MalAnimes?.FirstOrDefault()?.EndDate),
            "addeddate" or "sonarradded" => desc
                ? data.OrderByDescending(a => a.SonarrAnimes?.FirstOrDefault()?.AddedDate)
                : data.OrderBy(a => a.SonarrAnimes?.FirstOrDefault()?.AddedDate),
            "sizeondisk" => desc ? data.OrderByDescending(a => a.SonarrAnimes?.FirstOrDefault()?.SizeOnDisk ?? 0)
                                  : data.OrderBy(a => a.SonarrAnimes?.FirstOrDefault()?.SizeOnDisk ?? 0),
            "totalsizedisk" => desc ? data.OrderByDescending(a => a.SonarrAnimes?.Sum(s => s.SizeOnDisk ?? 0) ?? 0)
                                     : data.OrderBy(a => a.SonarrAnimes?.Sum(s => s.SizeOnDisk ?? 0) ?? 0),
            "episodes" => desc
                ? data.OrderByDescending(a =>
                    (a.SonarrAnimes?.FirstOrDefault()?.SeasonInfo ?? Array.Empty<SonarrSeasonInfo>())
                    .Where(s => s.SeasonNumber > 0)
                    .Sum(s => s.AvailableEpisodes))
                : data.OrderBy(a =>
                    (a.SonarrAnimes?.FirstOrDefault()?.SeasonInfo ?? Array.Empty<SonarrSeasonInfo>())
                    .Where(s => s.SeasonNumber > 0)
                    .Sum(s => s.AvailableEpisodes)),
            "avgepsize" => desc
                ? data.OrderByDescending(a =>
                {
                    var sonarr = a.SonarrAnimes?.FirstOrDefault();
                    var eps = (sonarr?.SeasonInfo ?? Array.Empty<SonarrSeasonInfo>())
                        .Where(s => s.SeasonNumber > 0)
                        .Sum(s => s.AvailableEpisodes);
                    var size = sonarr?.SizeOnDisk ?? 0;
                    return eps > 0 && size > 0 ? size / (long)eps : 0L;
                })
                : data.OrderBy(a =>
                {
                    var sonarr = a.SonarrAnimes?.FirstOrDefault();
                    var eps = (sonarr?.SeasonInfo ?? Array.Empty<SonarrSeasonInfo>())
                        .Where(s => s.SeasonNumber > 0)
                        .Sum(s => s.AvailableEpisodes);
                    var size = sonarr?.SizeOnDisk ?? 0;
                    return eps > 0 && size > 0 ? size / (long)eps : 0L;
                }),
            _ => data.OrderBy(a => a.Title)
        };
    }

    /// <summary>
    /// Returns true when a candidate title matches the search query either as a case-insensitive
    /// substring or via word-level fuzzy similarity above the given threshold.
    /// </summary>
    private static bool TitleMatches(string? candidate, string query, double threshold) =>
        !string.IsNullOrWhiteSpace(candidate) &&
        (candidate.Contains(query, StringComparison.OrdinalIgnoreCase) ||
         ComputeWordLevelFuzzySimilarity(candidate, query) >= threshold);

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
        if ((double)minLen / maxLen < MinLengthRatioForFuzzy) return 0.0;
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

            // Build entity-level coverage groups — uses the corrected logic that respects
            // the fact that one anime entity can map to multiple AniDbIds.
            var entityGroups = await LoadEntityCoverageAsync();

            // matchedCount = distinct AniDbIds that host more than one source type.
            var matchedCount = entityGroups.AniDbIdSources.Count(kv => kv.Value.Count > 1);

            // Gap counts operate at the SOURCE ENTITY level, not the AniDbId level.
            // An entity is a gap only when NONE of its AniDbIds overlaps with the target source's AniDbIds.
            var sonarrNotInMal  = entityGroups.AnimeEntityGroups.Count(g => g.Anime.AnimeSource == AnimeSource.Sonarr       && !g.IsLinkedToMal);
            var plexNotInMal    = entityGroups.AnimeEntityGroups.Count(g => g.Anime.AnimeSource == AnimeSource.Plex          && !g.IsLinkedToMal);

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
                plexNotInMal,
                genreStats,
                new ViewingProgressStats(malEpisodesWatched, malTotalEpisodes, plexEpisodesWatched, plexTotalEpisodes)
            ));
        }
        catch (Exception ex)
        {
            return Result.Failure<AnimeStatisticsResponse>(new(Constants.Errors.GetStatisticsAsync, ex.Message));
        }
    }

    public async Task<Result<IEnumerable<CoverageGapItemResponse>>> GetCoverageGapAsync(string gapType)
    {
        try
        {
            var entityGroups = await LoadEntityCoverageAsync();

            var normalised = gapType.ToLowerInvariant();

            // Determine which entities are true gaps.
            // An entity is a gap only when NONE of its AniDbIds is shared with an entity in the target source.
            // This means: if the Plex series maps to {AniDbId_7622, AniDbId_7623} and the MAL series maps to
            // {AniDbId_7622}, the Plex entity is correctly identified as NOT a gap — even though AniDbId_7623
            // has no MAL entry at all.
            IEnumerable<AnimeEntityCoverage> gapEntities = normalised switch
            {
                "plexnotinmal"    => entityGroups.AnimeEntityGroups.Where(g => g.Anime.AnimeSource == AnimeSource.Plex          && !g.IsLinkedToMal),
                "sonarrnotinmal"  => entityGroups.AnimeEntityGroups.Where(g => g.Anime.AnimeSource == AnimeSource.Sonarr         && !g.IsLinkedToMal),
                _                 => []
            };

            var plexBaseUrl   = await GetPlexBaseUrlAsync();
            var sonarrBaseUrl = await GetSonarrSeriesBaseUrlAsync();

            // Pre-load Plex cache for progress enrichment (only for plexnotinmal).
            var isPlexNeedle = normalised is "plexnotinmal";
            Dictionary<string, string> plexProgressCache = [];
            if (isPlexNeedle)
            {
                var plexCacheResult = await cacheEntryService.GetAsync(AnimeSource.Plex);
                if (plexCacheResult.IsSuccess)
                {
                    plexProgressCache = plexCacheResult.Value
                        .GroupBy(c => c.SourceId)
                        .ToDictionary(g => g.Key, g => g.Last().RawData);
                }
            }

            // Pre-load Sonarr cache for poster enrichment (sonarrnotinmal) and slug-based URL construction (all gap types).
            var isSonarrNeedle = normalised is "sonarrnotinmal";            Dictionary<string, string> sonarrImageCache = [];
            Dictionary<string, string?> sonarrSlugCache = [];
            {
                var sonarrCacheResult = await cacheEntryService.GetAsync(AnimeSource.Sonarr);
                if (sonarrCacheResult.IsSuccess)
                {
                    // Deduplicate by SourceId — use last entry if duplicates exist.
                    var sonarrEntries = sonarrCacheResult.Value
                        .GroupBy(c => c.SourceId)
                        .Select(g => g.Last())
                        .ToList();

                    if (isSonarrNeedle)
                        sonarrImageCache = sonarrEntries.ToDictionary(e => e.SourceId, e => e.RawData);

                    // Build SourceId → TitleSlug dictionary for correct Sonarr web-UI URLs.
                    foreach (var entry in sonarrEntries)
                    {
                        try
                        {
                            var s = JsonSerializer.Deserialize<SonarrSeries>(entry.RawData, JsonOptions);
                            sonarrSlugCache[entry.SourceId] = s?.TitleSlug;
                        }
                        catch (JsonException) { sonarrSlugCache[entry.SourceId] = null; }
                    }
                }
            }

            var results = gapEntities
                .Select(gap =>
                {
                    var anime = gap.Anime;

                    // Use the lexicographically smallest AniDbId as the canonical identifier.
                    var canonicalAniDbId = gap.AniDbIds.Min() ?? gap.AniDbIds.First();

                    var title = anime.Title ?? "NO_TITLE";

                    // The gap item IS present in its own source — build that URL.
                    string? malUrl = anime.AnimeSource == AnimeSource.MyAnimeList
                        ? $"{myAnimeListHost}/anime/{anime.SourceId}"
                        : null;

                    string? plexUrl = anime.AnimeSource == AnimeSource.Plex && !string.IsNullOrWhiteSpace(plexBaseUrl)
                        ? $"{plexBaseUrl}{anime.SourceId}"
                        : null;

                    string? sonarrUrl = anime.AnimeSource == AnimeSource.Sonarr && !string.IsNullOrWhiteSpace(sonarrBaseUrl)
                        ? $"{sonarrBaseUrl.TrimEnd('/')}/series/{(sonarrSlugCache.TryGetValue(anime.SourceId, out var sl) && sl != null ? sl : anime.SourceId)}"
                        : null;

                    // Cross-source enrichment: for Plex/MAL gaps also show if the item IS in Sonarr (via shared AniDbId).
                    if (anime.AnimeSource is AnimeSource.Plex or AnimeSource.MyAnimeList && gap.IsLinkedToSonarr && !string.IsNullOrWhiteSpace(sonarrBaseUrl))
                    {
                        var linkedSonarr = entityGroups.AnimeEntityGroups
                            .FirstOrDefault(g => g.Anime.AnimeSource == AnimeSource.Sonarr && g.AniDbIds.Overlaps(gap.AniDbIds));
                        if (linkedSonarr != null)
                        {
                            var linkedSlug = sonarrSlugCache.TryGetValue(linkedSonarr.Anime.SourceId, out var ls) && ls != null ? ls : linkedSonarr.Anime.SourceId;
                            sonarrUrl = $"{sonarrBaseUrl.TrimEnd('/')}/series/{linkedSlug}";
                        }
                    }

                    // For Sonarr gaps: also surface a linked Plex URL when available.
                    if (anime.AnimeSource == AnimeSource.Sonarr && gap.IsLinkedToPlex && !string.IsNullOrWhiteSpace(plexBaseUrl))
                    {
                        var linkedPlex = entityGroups.AnimeEntityGroups
                            .FirstOrDefault(g => g.Anime.AnimeSource == AnimeSource.Plex && g.AniDbIds.Overlaps(gap.AniDbIds));
                        if (linkedPlex != null)
                            plexUrl = $"{plexBaseUrl}{linkedPlex.Anime.SourceId}";
                    }

                    PlexProgressionInfo? plexProgress = null;
                    int? year = null;
                    string? originalTitle = null;
                    string? thumbnailUrl = null;
                    if (isPlexNeedle && plexProgressCache.TryGetValue(anime.SourceId, out var rawData))
                    {
                        try
                        {
                            var plexMedia = JsonSerializer.Deserialize<PlexMedia>(rawData, JsonOptions);
                            if (plexMedia != null)
                            {
                                plexProgress = new PlexProgressionInfo(plexMedia.ViewedLeafCount, plexMedia.LeafCount);
                                year = plexMedia.Year > 0 ? plexMedia.Year : null;
                                originalTitle = string.IsNullOrWhiteSpace(plexMedia.OriginalTitle) ? null : plexMedia.OriginalTitle;
                                if (!string.IsNullOrWhiteSpace(plexMedia.Thumb))
                                    thumbnailUrl = $"/api/v1/animes/plex-thumb/{Uri.EscapeDataString(anime.SourceId)}";
                            }
                        }
                        catch (JsonException) { /* skip if deserialization fails */ }
                    }

                    if (isSonarrNeedle && sonarrImageCache.TryGetValue(anime.SourceId, out var sonarrRawData))
                    {
                        try
                        {
                            var sonarrSeries = JsonSerializer.Deserialize<SonarrSeries>(sonarrRawData, JsonOptions);
                            thumbnailUrl = sonarrSeries?.Images?
                                .FirstOrDefault(i => string.Equals(i.CoverType, SonarrCoverType, StringComparison.OrdinalIgnoreCase))
                                ?.RemoteUrl;
                            if (sonarrSeries?.Year > 0)
                                year = sonarrSeries.Year;
                        }
                        catch (JsonException) { /* skip */ }
                    }

                    return new CoverageGapItemResponse(canonicalAniDbId, title, malUrl, plexUrl, sonarrUrl, plexProgress, year, originalTitle, thumbnailUrl);
                })
                .OrderBy(r => r.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return Result.Success<IEnumerable<CoverageGapItemResponse>>(results);
        }
        catch (Exception ex)
        {
            return Result.Failure<IEnumerable<CoverageGapItemResponse>>(new(Constants.Errors.GetStatisticsAsync, ex.Message));
        }
    }

    /// <summary>
    /// Loads all AnimeMapping rows and builds entity-level coverage groups in one pass.
    /// The result exposes per-AniDbId source sets AND per-anime-entity cross-source link flags.
    /// This is the correct architectural source of truth for gap calculations: it respects the
    /// fact that a single anime entity (e.g., a Plex series) can map to multiple AniDbIds in
    /// Kometa, and a gap only exists when NONE of those AniDbIds are shared with the target source.
    /// </summary>
    private async Task<EntityCoverageResult> LoadEntityCoverageAsync()
    {
        var allMappingsResult = await animeMappingService.GetAllAsync(includeAnime: true);
        var validMappings = allMappingsResult.IsSuccess
            ? allMappingsResult.Value.Where(m => m.Anime != null).ToList()
            : [];

        // Step 1: For each AniDbId, collect which source types are present.
        var aniDbIdSources = validMappings
            .GroupBy(m => m.AniDbId)
            .ToDictionary(g => g.Key, g => g.Select(m => m.Anime!.AnimeSource).ToHashSet());

        // Step 2: Build sets of AniDbIds that have at least one entry for each source.
        var malLinkedAniDbIds    = aniDbIdSources.Where(kv => kv.Value.Contains(AnimeSource.MyAnimeList)).Select(kv => kv.Key).ToHashSet();
        var plexLinkedAniDbIds   = aniDbIdSources.Where(kv => kv.Value.Contains(AnimeSource.Plex)).Select(kv => kv.Key).ToHashSet();
        var sonarrLinkedAniDbIds = aniDbIdSources.Where(kv => kv.Value.Contains(AnimeSource.Sonarr)).Select(kv => kv.Key).ToHashSet();

        // Step 3: Group by anime entity and compute cross-source link status.
        var entityGroups = validMappings
            .GroupBy(m => m.AnimeId)
            .Select(g =>
            {
                var anime = g.First().Anime!;
                var myAniDbIds = g.Select(m => m.AniDbId).ToHashSet();
                return new AnimeEntityCoverage(
                    Anime:            anime,
                    AniDbIds:         myAniDbIds,
                    // An entity is "linked" to a source if ANY of its AniDbIds appears in that source's set.
                    IsLinkedToMal:    myAniDbIds.Overlaps(malLinkedAniDbIds),
                    IsLinkedToPlex:   myAniDbIds.Overlaps(plexLinkedAniDbIds),
                    IsLinkedToSonarr: myAniDbIds.Overlaps(sonarrLinkedAniDbIds)
                );
            })
            .ToList();

        return new EntityCoverageResult(aniDbIdSources, entityGroups);
    }

    public async Task<Result> SyncAllAsync(bool forceRefreshKometaMapping = false)
    {
        try
        {
            var matchResult = await animeMatchingService.MatchAsync(forceRefreshKometaMapping);
            if (matchResult.IsFailure)
                return Result.Failure(matchResult.Error);

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

            var plexEnabled = await IsPlexEnabledAsync();
            var sonarrEnabled = await IsSonarrEnabledAsync();

            var plexAnimes = plexEnabled
                ? await FetchPlexAnimeDetailsAsync(animeMappings.Where(m => m.Anime?.AnimeSource == AnimeSource.Plex))
                : [];

            var malAnimes = await FetchMalAnimeDetailsAsync(animeMappings.Where(m => m.Anime?.AnimeSource == AnimeSource.MyAnimeList));

            var sonarrAnimes = sonarrEnabled
                ? await FetchSonarrAnimeDetailsAsync(animeMappings.Where(m => m.Anime?.AnimeSource == AnimeSource.Sonarr))
                : [];

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

    public async Task<Result<AnimeDetailsResponse?>> GetByMalIdAsync(string malId)
    {
        var aniDbIdResult = await animeMappingService.GetAniDbIdByMalIdAsync(malId);
        if (aniDbIdResult.IsFailure)
            return Result.Failure<AnimeDetailsResponse?>(aniDbIdResult.Error);

        if (aniDbIdResult.Value != null)
            return await GetByIdAsync(aniDbIdResult.Value);

        // Anime not found in local DB — fall back to live MAL API for a minimal response.
        try
        {
            var malResult = await malAnimeSource.GetByIdAsync(malId);
            if (malResult.IsFailure || malResult.Value == null)
                return Result.Success<AnimeDetailsResponse?>(null);

            var malInfo = new MalAnimeInfo(malResult.Value, myAnimeListHost);
            var response = new AnimeDetailsResponse(
                malId,
                malResult.Value.Title ?? "Unknown",
                malResult.Value.Synopsis,
                [],
                [malInfo],
                []);
            return Result.Success<AnimeDetailsResponse?>(response);
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

            var sonarrBaseUrl = await GetSonarrSeriesBaseUrlAsync();
            var responses = searchResult.Value.Select(r => new SonarrCodecSearchResponse(
                r.Id,
                r.Title,
                $"{sonarrBaseUrl}/series/{r.Id}",
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

            var enriched = await EnrichMalItemsAsync(seasonalResult.Value, showNsfw);
            return Result.Success(enriched);
        }
        catch (Exception ex)
        {
            return Result.Failure<IEnumerable<SeasonalAnimeResponse>>(new(Constants.Errors.GetAllAsync, ex.Message));
        }
    }

    private async Task<IEnumerable<SeasonalAnimeResponse>> EnrichMalItemsAsync(IList<MalAnime> items, bool showNsfw)
    {
        var seasonalMalIds = items.Select(a => a.Id.ToString()).ToList();

        // ── Kometa lookup: malIds → aniDbId + tvdbId ─────────────────────────────────
        // Single self-join query treats the Kometa mapping as the sole source of truth.
        // TvdbId drives the exact `tvdb:XXXXX` Sonarr lookup; AniDbId drives provider state.
        var malToAniDb = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
                var animes = aniDbGroup.Select(m => m.Anime!).ToList();
                var plexEntry = animes.FirstOrDefault(a => a.AnimeSource == AnimeSource.Plex);
                var sonarrEntry = animes.FirstOrDefault(a => a.AnimeSource == AnimeSource.Sonarr);
                var malEntry = animes.FirstOrDefault(a => a.AnimeSource == AnimeSource.MyAnimeList);
                bool hasPlex = plexEntry != null;
                if (hasPlex) anyInPlex = true;
                aniDbToSources[aniDbGroup.Key] = (hasPlex, plexEntry?.SourceId, sonarrEntry != null, sonarrEntry?.SourceId, malEntry != null, malEntry?.SourceId);
            }
        }

        // Resolve Plex base URL only if needed
        string? plexBaseUrl = anyInPlex ? await GetPlexBaseUrlAsync() : null;
        var sonarrSeriesBaseUrl = await GetSonarrSeriesBaseUrlAsync();

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

        return filteredItems.Select(anime =>
        {
            var malId = anime.Id.ToString();
            malToAniDb.TryGetValue(malId, out var aniDbId);
            var sources = aniDbId != null && aniDbToSources.TryGetValue(aniDbId, out var s) ? s : default;
            var inMal = sources.HasMal;
            var inSonarr = sources.HasSonarr;
            var inPlex = sources.HasPlex;
            // plexBaseUrl ends with %2F (URL-encoded "/"), so appending source ID
            // directly produces the correct path: .../metadata/{ratingKey}
            string? plexUrl = inPlex && plexBaseUrl != null && sources.PlexId != null
                ? $"{plexBaseUrl}{sources.PlexId}" : null;
            // Use TitleSlug from cache (same as regular anime card); fall back to numeric ID
            var sonarrSlug = sources.SonarrId != null && sonarrSlugById.TryGetValue(sources.SonarrId, out var slug)
                ? slug : sources.SonarrId;
            string? sonarrUrl = inSonarr && sonarrSlug != null && !string.IsNullOrWhiteSpace(sonarrSeriesBaseUrl)
                ? $"{sonarrSeriesBaseUrl.TrimEnd('/')}/series/{sonarrSlug}" : null;
            var tvdbId = aniDbId != null && aniDbToTvdb.TryGetValue(aniDbId, out var tId) ? tId : null;
            string? malViewState = sources.MalId != null && malViewStateById.TryGetValue(sources.MalId, out var vs) ? vs : null;
            return new SeasonalAnimeResponse(
                MalId: malId,
                Title: anime.Title ?? string.Empty,
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
                MalViewState: malViewState,
                AniDbId: aniDbId,
                AgeRating: anime.Rating
            );
        });
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
            var result = await sonarrAnimeSource.SearchLookupAsync(term);
            if (result.IsFailure)
                return result;

            var items = result.Value;
            if (items.Count == 0)
                return Result.Success(items);

            // ── Enrich with AnimeDbId (DB GUID) for library items ──────────────────────────
            var sourceIdToGuid = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            var libraryItems = items.Where(i => i.SonarrSeriesId > 0).ToList();
            if (libraryItems.Count > 0)
            {
                var sonarrAnimes = await animeRepository.GetAllBySourceAsync(AnimeSource.Sonarr);
                foreach (var a in sonarrAnimes)
                    sourceIdToGuid[a.SourceId] = a.Id;
            }

            // ── Enrich with MalId via Kometa: TvdbId → AniDbId → MalId ────────────────────
            // Batch query 1: all unique TvdbIds → Kometa TVDB mappings
            var tvdbIds = items
                .Where(i => i.TvdbId > 0)
                .Select(i => i.TvdbId.ToString())
                .Distinct()
                .ToList();

            var tvdbToMalId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (tvdbIds.Count > 0)
            {
                var tvdbKometa = await kometaAnimeMappingRepository.GetByPlatformIdsAsync(KometaAnimePlatform.Tvdb, tvdbIds);
                var aniDbIds = tvdbKometa.Select(k => k.AniDbId).Distinct().ToList();

                if (aniDbIds.Count > 0)
                {
                    // Batch query 2: AniDbIds → MAL platform mappings
                    var malKometa = await kometaAnimeMappingRepository.GetByAniDbIdsAndPlatformAsync(KometaAnimePlatform.Mal, aniDbIds);

                    // Map AniDbId → MalId (first found wins per AniDbId)
                    var aniDbToMalId = malKometa
                        .GroupBy(k => k.AniDbId, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.First().PlatformId, StringComparer.OrdinalIgnoreCase);

                    // Map TvdbId → MalId via AniDbId
                    foreach (var kometaEntry in tvdbKometa)
                    {
                        if (aniDbToMalId.TryGetValue(kometaEntry.AniDbId, out var malId)
                            && !tvdbToMalId.ContainsKey(kometaEntry.PlatformId))
                        {
                            tvdbToMalId[kometaEntry.PlatformId] = malId;
                        }
                    }
                }
            }

            // ── Apply enrichments ──────────────────────────────────────────────────────────
            items = items.Select(i =>
            {
                Guid? animeDbId = i.SonarrSeriesId > 0
                    && sourceIdToGuid.TryGetValue(i.SonarrSeriesId.ToString(), out var dbId)
                    ? dbId : null;

                string? malId = tvdbToMalId.TryGetValue(i.TvdbId.ToString(), out var mid) ? mid : null;

                return (animeDbId == null && malId == null)
                    ? i
                    : i with { AnimeDbId = animeDbId, MalId = malId };
            }).ToList();

            return Result.Success(items);
        }
        catch (Exception ex)
        {
            return Result.Failure<IList<SonarrLookupItem>>(new(Constants.Errors.GetAllAsync, ex.Message));
        }
    }

    public async Task<Result<SonarrAddResult>> AddSonarrSeriesAsync(int tvdbId, string rootFolderPath, int qualityProfileId, string monitor = "all", bool seasonFolder = true, string? malId = null)
    {
        try
        {
            // Get the configured tag IDs to apply to the new series
            var optionsResult = await sonarrAnimeSource.GetOptionsAsync();
            var tagIds = optionsResult.IsSuccess ? optionsResult.Value.AnimeTagIds : new List<int>();

            var addResult = await sonarrAnimeSource.AddSeriesAsync(tvdbId, rootFolderPath, qualityProfileId, tagIds, monitor, seasonFolder);
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

            var sonarrBaseUrl = await GetSonarrSeriesBaseUrlAsync();
            var url = !string.IsNullOrEmpty(added.TitleSlug)
                ? $"{sonarrBaseUrl.TrimEnd('/')}/series/{added.TitleSlug}"
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

    public async Task<Result> UpdateMalListStatusAsync(string malId, string status, int numWatchedEpisodes, int score, string? sourceAniDbId = null)
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
                    // Look up AniDbId via Kometa (malId → AniDbId).
                    // Use ALL Kometa AniDbIds for this MAL entry, plus the sourceAniDbId
                    // (passed from the gap-confirm flow). This ensures the link is created
                    // even when Kometa's MAL mapping uses a different AniDbId than the
                    // Sonarr entry (which is mapped via TVDB).
                    var malMappings = await kometaAnimeMappingRepository.GetByPlatformIdsAsync(
                        MetaAnime.Domain.Enums.KometaAnimePlatform.Mal, new[] { malId });
                    var aniDbIds = malMappings.Select(m => m.AniDbId).ToHashSet();
                    if (sourceAniDbId != null)
                        aniDbIds.Add(sourceAniDbId);

                    var animeForDb = malAnime.ToAnime();
                    await animeRepository.AddAsync(animeForDb);

                    foreach (var aniDbId in aniDbIds)
                        await animeMappingRepository.AddAsync(new AnimeMapping { AniDbId = aniDbId, AnimeId = animeForDb.Id });
                }
                else if (sourceAniDbId != null)
                {
                    // MAL anime already exists (synced from user's list) but the gap-confirm
                    // flow supplied a sourceAniDbId — create a bridge AnimeMapping so the
                    // coverage gap resolver sees the overlap immediately.
                    await animeMappingRepository.AddAsync(new AnimeMapping { AniDbId = sourceAniDbId, AnimeId = existingAnime.Id });
                }

                // Persist the cross-link so it survives the full delete-and-recreate sync cycle.
                if (sourceAniDbId != null)
                    await crossLinkService.AddAsync(sourceAniDbId, malId);
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

    /// <summary>
    /// Filters anime by a tag set (genre, theme, or demographic) resolved from the MAL cache.
    /// All three tag categories are stored in the MAL Genres array; the caller supplies the
    /// appropriate selected values and AND/OR operator string.
    /// </summary>
    private IEnumerable<AnimeDetailsResponse> ApplyTagsFilter(
        IEnumerable<AnimeDetailsResponse> data,
        string[]? selectedValues,
        string? op,
        Dictionary<string, string> malCache)
    {
        if (selectedValues is not { Length: > 0 }) return data;

        var tagSet = new HashSet<string>(selectedValues, StringComparer.OrdinalIgnoreCase);
        bool isAnd = FilterConstants.Operator.And.Equals(op, StringComparison.OrdinalIgnoreCase);

        return data.Where(a =>
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
                    var malAnime = JsonSerializer.Deserialize<MalAnime>(malJson, JsonOptions);
                    var animeGenres = malAnime?.Genres?.Select(g => g.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    if (animeGenres == null) return false;

                    return isAnd
                        ? tagSet.All(g => animeGenres.Contains(g))
                        : tagSet.Any(g => animeGenres.Contains(g));
                }
                catch (JsonException ex)
                {
                    logger.LogWarning(Constants.LogMessages.AnimeMediatorService.MalDeserializeFailedGenreFilter, m.Id, ex.Message);
                    return false;
                }
            });
        });
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
        return TitleSimilarityDefault;
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
        return malAnime.Genres?.Any(g => MalConstants.NsfwGenres.Contains(g.Name)) == true;
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

    private SonarrAnimeInfo CreateSonarrAnimeInfoFromCache(AnimeMapping mapping, Dictionary<string, string> sonarrCache, string sonarrBaseUrl)
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
                        return new SonarrAnimeInfo(sonarrSeries, sonarrBaseUrl, sonarrSeries.CodecDistribution);
                    }
                }
                catch (JsonException ex)
                {
                    logger.LogWarning(ex, Constants.LogMessages.AnimeMediatorService.SonarrCacheDeserializeFailed, mapping.Anime.SourceId);
                }
            }
        }
        return new SonarrAnimeInfo(mapping.Anime, sonarrBaseUrl);
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
        try
        {
            var sonarrBaseUrl = await GetSonarrSeriesBaseUrlAsync();
            if (string.IsNullOrWhiteSpace(sonarrBaseUrl))
            {
                logger.LogWarning("Sonarr enrichment skipped: Sonarr base URL is not configured");
                return [];
            }

            var tasks = mappings.Select(async mapping =>
            {
                var sonarrResult = await sonarrAnimeSource.GetByIdAsync(mapping.Anime!.SourceId);
                if (sonarrResult.IsSuccess && sonarrResult.Value != null)
                {
                    var codecDistribution = await sonarrAnimeSource.GetCodecDistributionAsync(mapping.Anime!.SourceId);
                    return new SonarrAnimeInfo(sonarrResult.Value, sonarrBaseUrl, codecDistribution);
                }
                else
                {
                    return new SonarrAnimeInfo(mapping.Anime, sonarrBaseUrl);
                }
            });

            return (await Task.WhenAll(tasks)).ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Sonarr enrichment failed and was skipped: {Message}", ex.Message);
            return [];
        }
    }

    #endregion

    public async Task<Result<FranchiseConstellationResponse>> GetFranchiseConstellationAsync(string malId)
    {
        var result = await malAnimeSource.GetWithRelatedAnimeAsync(malId);
        if (result.IsFailure)
            return Result.Failure<FranchiseConstellationResponse>(result.Error);

        var anime = result.Value;
        var relations = (anime.RelatedAnime ?? [])
            .Where(r => r.Node != null)
            .Select(r => new FranchiseRelationDto(
                r.Node!.Id.ToString(),
                r.Node.Title ?? string.Empty,
                r.Node.MainPicture?.Large ?? r.Node.MainPicture?.Medium,
                r.RelationType,
                r.RelationTypeFormatted,
                r.Node.MediaType,
                r.Node.Status,
                r.Node.MyListStatus?.Status
            ));

        return Result.Success(new FranchiseConstellationResponse(
            anime.Id.ToString(),
            anime.Title ?? string.Empty,
            anime.MainPicture?.Large ?? anime.MainPicture?.Medium,
            anime.Synopsis,
            anime.StartDate,
            anime.EndDate,
            relations,
            anime.MediaType,
            anime.Status,
            anime.MyListStatus?.Status
        ));
    }

    public Task<Result<IList<SonarrTagInfo>>> GetAllSonarrTagsAsync()
        => sonarrAnimeSource.GetAllTagsAsync();

    public Task<Result<SonarrTagInfo>> CreateSonarrTagAsync(string label)
        => sonarrAnimeSource.CreateTagAsync(label);

    public async Task<Result<IList<MalSearchResultItem>>> SearchMalAnimeAsync(string query)
    {
        var searchResult = await malAnimeSource.SearchByTitleAsync(query);
        if (searchResult.IsFailure)
            return searchResult;

        // Enrich each result with IsAlreadyInLibrary — true when that MAL ID is already
        // linked to a unified Anime entity in our DB (so the user knows it's already mapped).
        var existingMalAnimesResult = await animeService.GetAllBySourceAsync(AnimeSource.MyAnimeList);
        var existingMalIds = existingMalAnimesResult.IsSuccess
            ? existingMalAnimesResult.Value.Select(a => a.SourceId).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : [];

        var enriched = searchResult.Value
            .Select(r => r with { IsAlreadyInLibrary = existingMalIds.Contains(r.Id) })
            .ToList();

        return Result.Success<IList<MalSearchResultItem>>(enriched);
    }

    public async Task<Result<IEnumerable<SeasonalAnimeResponse>>> SearchMalAnimeWithDetailsAsync(string query, int limit = 100)
    {
        try
        {
            bool showNsfw = await IsNsfwAllowedAsync();
            var searchResult = await malAnimeSource.SearchByTitleRichAsync(query, limit);
            if (searchResult.IsFailure)
                return Result.Failure<IEnumerable<SeasonalAnimeResponse>>(searchResult.Error);

            var enriched = await EnrichMalItemsAsync(searchResult.Value, showNsfw);
            return Result.Success(enriched);
        }
        catch (Exception ex)
        {
            return Result.Failure<IEnumerable<SeasonalAnimeResponse>>(new(Constants.Errors.SearchMalAnimeWithDetailsAsync, ex.Message));
        }
    }

    private async Task<bool> IsPlexEnabledAsync()
    {
        try
        {
            var plexEnabledEntry = await propertyBagRepository.GetByKeyAsync(SettingsKeys.PlexEnabled);
            return plexEnabledEntry != null && bool.TryParse(plexEnabledEntry.Value, out var isEnabled) && isEnabled;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to check Plex enabled status");
            return false;
        }
    }

    private async Task<bool> IsSonarrEnabledAsync()
    {
        try
        {
            var sonarrEnabledEntry = await propertyBagRepository.GetByKeyAsync(SettingsKeys.SonarrEnabled);
            return sonarrEnabledEntry != null && bool.TryParse(sonarrEnabledEntry.Value, out var isEnabled) && isEnabled;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to check Sonarr enabled status");
            return false;
        }
    }

    // -----------------------------------------------------------------------
    // Private types — used only by entity-level gap logic
    // -----------------------------------------------------------------------

    /// <summary>Coverage data for a single anime entity (one Plex/MAL/Sonarr record).</summary>
    private sealed record AnimeEntityCoverage(
        Anime            Anime,
        HashSet<string>  AniDbIds,
        bool             IsLinkedToMal,
        bool             IsLinkedToPlex,
        bool             IsLinkedToSonarr
    );

    /// <summary>Aggregated coverage result shared by GetStatisticsAsync and GetCoverageGapAsync.</summary>
    private sealed record EntityCoverageResult(
        Dictionary<string, HashSet<AnimeSource>> AniDbIdSources,
        List<AnimeEntityCoverage>                AnimeEntityGroups
    );

}

