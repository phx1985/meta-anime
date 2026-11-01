using MetaAnime.Application.Utilities;
using MetaAnime.Core;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Interfaces.Shared.Results;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Models;
using MetaAnime.Core.Sync;
using MetaAnime.Domain.Entities;
using MetaAnime.Domain.Enums;
using MetaAnime.Infrastructure.Kometa.Models;
using MetaAnime.Infrastructure.Kometa.Utilities.Extensions;
using MetaAnime.Infrastructure.MyAnimeList.Models;
using MetaAnime.Infrastructure.Plex.Models;
using MetaAnime.Infrastructure.SonarrAPIs.Sonarr.Models;
using MetaAnime.Infrastructure.Utilities.Extensions;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;
using Anime = MetaAnime.Domain.Entities.Anime;
using AnimeMapping = MetaAnime.Domain.Entities.AnimeMapping;
using CacheEntry = MetaAnime.Domain.Entities.CacheEntry;
using KometaAnimeMapping = MetaAnime.Domain.Entities.KometaAnimeMapping;

namespace MetaAnime.Application.Services;

public sealed class AnimeMatchingService(
    IKometaAnimeJsonService<KometaRaw> kometaAnimeJsonService,
    IKometaAnimeMappingService kometaAnimeMappingService,
    IAnimeMappingService mappingService,
    IAnimeService animeService,
    ICacheEntryService cacheEntryService,
    ICrossLinkService crossLinkService,
    IPropertyBagRepository propertyBagRepository,
    ILogger<AnimeMatchingService> logger)
    : IAnimeMatchingService
{
    /// <summary>Minimum symmetric word-level similarity to consider a title-only candidate.</summary>
    private const double TitleOnlyThreshold = 0.8;

    /// <summary>Minimum symmetric word-level similarity for a StrongTitle+Year match.</summary>
    private const double StrongTitleThreshold = 0.9;

    /// <summary>Maximum |year difference| for year corroboration.</summary>
    private const int YearTolerance = 1;

    private const int MaxReportedCandidates = 3;

    private static readonly JsonSerializerOptions CacheReadOptions = new() { PropertyNameCaseInsensitive = true };

    // ── Rich metadata extracted from the provider cache blobs ───────────────────

    private sealed record AnimeMeta(
        List<string> NormalizedTitles,
        int? Year,
        int? Episodes,
        int? SeasonMarker);

    private sealed record ScoredCandidate(
        Anime Candidate,
        AnimeMeta Meta,
        double Similarity,
        int? YearDelta,
        int? EpisodeDelta,
        MatchConfidence Confidence,
        List<string> BorrowedAniDbIds);

    public async Task<Result<MatchReport>> MatchAsync(bool forceRefreshKometaMapping = false, ISyncProgressNotifier? progressNotifier = null)
    {
        var sw = Stopwatch.StartNew();

        // ── Step 1: Kometa mappings (incremental, conditional fetch) ───────────
        await NotifyAsync(progressNotifier, SyncStepKeys.Kometa, SyncStepState.Running, 0, sw);

        var kometaResult = await RefreshKometaMappingsAsync(forceRefreshKometaMapping);
        if (kometaResult.IsFailure)
        {
            await NotifyAsync(progressNotifier, SyncStepKeys.Kometa, SyncStepState.Error, null, sw, kometaResult.Error.Message);
            return Result.Failure<MatchReport>(kometaResult.Error);
        }

        await NotifyAsync(progressNotifier, SyncStepKeys.Kometa, SyncStepState.Done, 100, sw);
        sw.Restart();

        // ── Step 2: Fetch from sources (MAL + Plex + Sonarr concurrently) ──────
        // Per-source Running/Done/Error notifications are emitted inside RefreshData.
        var refreshDataResult = await animeService.RefreshData(progressNotifier);
        if (refreshDataResult.IsFailure)
        {
            return Result.Failure<MatchReport>(refreshDataResult.Error);
        }

        sw.Restart();

        // ── Step 3: Match anime across sources ───────────────────────────────
        await NotifyAsync(progressNotifier, SyncStepKeys.Match, SyncStepState.Running, 0, sw);
        var phase = Stopwatch.StartNew();

        var kometaMappingsResult = await kometaAnimeMappingService.GetAllAsync();
        if (kometaMappingsResult.IsFailure)
        {
            await NotifyAsync(progressNotifier, SyncStepKeys.Match, SyncStepState.Error, null, sw, kometaMappingsResult.Error.Message);
            return Result.Failure<MatchReport>(kometaMappingsResult.Error);
        }

        var allAnimes = refreshDataResult.Value.ToList();

        // O(K) pre-index: instant (platform, platformId) → Kometa row lookups instead of
        // O(A×K) Where scans with a string-switch platform parse per comparison.
        var kometaIndex = BuildKometaIndex(kometaMappingsResult.Value);
        var indexMs = phase.ElapsedMilliseconds;
        phase.Restart();

        var metaByAnimeId = await BuildAnimeMetaAsync(allAnimes);
        var metaMs = phase.ElapsedMilliseconds;
        phase.Restart();

        // ── 3a: exact ID matching ────────────────────────────────────────────
        List<AnimeMapping> animeMappings = [];
        var exactPairs = new HashSet<(Guid AnimeId, string AniDbId)>();
        var seenPairs = new HashSet<(Guid AnimeId, string AniDbId)>();
        var resolvedRows = new Dictionary<Guid, List<KometaAnimeMapping>>();
        List<Anime> unresolvedAnimes = [];

        // Platforms are ordered by priority (Tvdb > Imdb > Tmdb)
        (MediaPlatform MediaPlatform, KometaAnimePlatform KometaPlatform)[] platformMappings =
        [
            (MediaPlatform.Tvdb, KometaAnimePlatform.Tvdb),
            (MediaPlatform.Imdb, KometaAnimePlatform.Imdb),
            (MediaPlatform.Tmdb, KometaAnimePlatform.TmdbShow),
        ];

        foreach (var anime in allAnimes)
        {
            List<KometaAnimeMapping>? rows = null;

            if (anime.AnimeSource == AnimeSource.MyAnimeList)
            {
                kometaIndex.TryGetValue((KometaAnimePlatform.Mal, anime.SourceId), out rows);
            }
            else if (anime.ExternalAnimeIdentifiers is { Count: > 0 })
            {
                foreach (var (mediaPlatform, kometaPlatform) in platformMappings)
                {
                    var identifier = anime.ExternalAnimeIdentifiers
                        .FirstOrDefault(ea => ea.MediaPlatform == mediaPlatform);
                    if (identifier is null)
                        continue;

                    // Only stop if this platform produced actual Kometa entries.
                    // If the platform ID exists but has no Kometa mapping, fall through to
                    // the next lower-priority platform (e.g. TVDB miss → try IMDB).
                    if (kometaIndex.TryGetValue((kometaPlatform, identifier.MediaPlatformId), out var found) && found.Count > 0)
                    {
                        rows = found;
                        break;
                    }
                }
            }

            if (rows is { Count: > 0 })
            {
                resolvedRows[anime.Id] = rows;
                foreach (var aniDbId in rows.Select(r => r.AniDbId).Distinct())
                {
                    if (exactPairs.Add((anime.Id, aniDbId)))
                    {
                        seenPairs.Add((anime.Id, aniDbId));
                        animeMappings.Add(new AnimeMapping { AnimeId = anime.Id, AniDbId = aniDbId });
                    }
                }
            }
            else
            {
                unresolvedAnimes.Add(anime);
            }
        }

        // ── 3b: fuzzy title fallback with year/episode corroboration ─────────
        // Bridges unmatched rows to rows that resolved through Kometa IDs by borrowing
        // their AniDbIds (the automated counterpart of manual cross-links). Only
        // unambiguous StrongTitle+Year candidates are auto-persisted; weaker candidates
        // are returned in the report for a future ambiguity queue.
        var idPassMs = phase.ElapsedMilliseconds;
        phase.Restart();
        var resolvedPool = allAnimes
            .Where(a => resolvedRows.ContainsKey(a.Id))
            .ToList();

        int strongMappings = 0, titleOnlyHints = 0;
        List<UnmatchedCandidate> unmatchedCandidates = [];

        foreach (var unresolved in unresolvedAnimes)
        {
            var meta = metaByAnimeId[unresolved.Id];
            var scored = new List<ScoredCandidate>();

            foreach (var candidate in resolvedPool)
            {
                if (candidate.AnimeSource == unresolved.AnimeSource)
                    continue;

                var candidateMeta = metaByAnimeId[candidate.Id];
                var similarity = ScoreTitles(meta, candidateMeta);
                if (similarity < TitleOnlyThreshold)
                    continue;

                if (!TryResolveBridgeRows(meta, candidateMeta, resolvedRows[candidate.Id], out var borrowedAniDbIds))
                    continue;

                var yearDelta = meta.Year is { } y1 && candidateMeta.Year is { } y2 ? Math.Abs(y1 - y2) : (int?)null;
                var episodeDelta = meta.Episodes is { } e1 && candidateMeta.Episodes is { } e2 ? Math.Abs(e1 - e2) : (int?)null;

                // Year/episode counts are tie-breakers, not gates: a missing year never
                // demotes an otherwise strong match (new shows often have no year on one
                // side yet), but a *conflicting* year is strong evidence of a different
                // show (remakes) and always demotes to TitleOnly.
                var yearConflict = yearDelta is > YearTolerance;
                var yearCorroborated = yearDelta is not null and <= YearTolerance;
                var confidence = !yearConflict
                    && similarity >= StrongTitleThreshold
                    && (similarity >= 1.0 || yearCorroborated)
                    ? MatchConfidence.StrongTitleAndYear
                    : MatchConfidence.TitleOnly;

                scored.Add(new ScoredCandidate(candidate, candidateMeta, similarity, yearDelta, episodeDelta, confidence, borrowedAniDbIds));
            }

            var strong = scored
                .Where(s => s.Confidence == MatchConfidence.StrongTitleAndYear)
                .OrderByDescending(s => s.Similarity)
                .ThenBy(s => s.YearDelta ?? int.MaxValue)
                .ThenBy(s => s.EpisodeDelta ?? int.MaxValue)
                .ToList();

            if (strong.Count > 0 && TrySelectBridge(strong, out var bridgeAniDbIds))
            {
                foreach (var aniDbId in bridgeAniDbIds)
                {
                    if (seenPairs.Add((unresolved.Id, aniDbId)))
                    {
                        animeMappings.Add(new AnimeMapping { AnimeId = unresolved.Id, AniDbId = aniDbId });
                        strongMappings++;
                    }
                }

                continue;
            }

            // No unambiguous strong bridge — report the row with its best candidates.
            var hints = scored
                .OrderByDescending(s => s.Similarity)
                .ThenBy(s => s.YearDelta ?? int.MaxValue)
                .ThenBy(s => s.EpisodeDelta ?? int.MaxValue)
                .Take(MaxReportedCandidates)
                .Select(s => new MatchCandidateHint(
                    s.Candidate.AnimeSource.ToString(),
                    s.Candidate.SourceId,
                    s.Candidate.Title,
                    s.Confidence,
                    Math.Round(s.Similarity, 3)))
                .ToList();

            titleOnlyHints += hints.Count(h => h.Confidence == MatchConfidence.TitleOnly);
            unmatchedCandidates.Add(new UnmatchedCandidate(
                unresolved.AnimeSource.ToString(),
                unresolved.SourceId,
                unresolved.Title,
                hints));
        }

        // ── 3c: user-confirmed cross-links ───────────────────────────────────
        var fuzzyMs = phase.ElapsedMilliseconds;
        phase.Restart();
        // Each cross-link maps a Sonarr/Plex AniDbId to a MAL anime; we create
        // an AnimeMapping for the MAL anime row using the Sonarr AniDbId.
        // If Kometa now natively covers a cross-link (exact ID path only), the bridge is
        // redundant — prune it so the system falls back to the normal Kometa path. Bridges
        // produced by fuzzy matching never prune a manual override.
        var crossLinks = await crossLinkService.GetAllAsync();
        if (crossLinks.Count > 0)
        {
            var malAnimeLookup = allAnimes
                .Where(a => a.AnimeSource == AnimeSource.MyAnimeList)
                .ToDictionary(a => a.SourceId ?? "");

            foreach (var link in crossLinks)
            {
                if (!malAnimeLookup.TryGetValue(link.MalId, out var malAnime))
                    continue;

                if (exactPairs.Contains((malAnime.Id, link.SourceAniDbId)))
                {
                    // Kometa now produces this mapping natively — cross-link is obsolete.
                    await crossLinkService.RemoveAsync(link.SourceAniDbId, link.MalId);
                }
                else if (animeMappings.All(m => m.AnimeId != malAnime.Id || m.AniDbId != link.SourceAniDbId))
                {
                    animeMappings.Add(new AnimeMapping { AnimeId = malAnime.Id, AniDbId = link.SourceAniDbId });
                }
            }
        }

        // Diff-based persistence: unchanged (AnimeId, AniDbId) pairs are never rewritten.
        var crossLinkMs = phase.ElapsedMilliseconds;
        phase.Restart();
        var addMappingsResult = await mappingService.MergeAsync(animeMappings);
        if (addMappingsResult.IsFailure)
        {
            await NotifyAsync(progressNotifier, SyncStepKeys.Match, SyncStepState.Error, null, sw, addMappingsResult.Error.Message);
            return Result.Failure<MatchReport>(addMappingsResult.Error);
        }

        // Capture the completion time immediately after all matching work is done.
        // Persist it before notifying clients so that any frontend poll triggered
        // by the Done event will always see the saved date.
        var completedAt = DateTime.UtcNow.ToString("O");
        await propertyBagRepository.UpsertAsync(SettingsKeys.LastSyncDate, completedAt);

        await NotifyAsync(progressNotifier, SyncStepKeys.Match, SyncStepState.Done, 100, sw);

        logger.LogInformation(
            Constants.LogMessages.AnimeMatchingService.MatchTimings,
            indexMs, metaMs, idPassMs, fuzzyMs, crossLinkMs, phase.ElapsedMilliseconds);

        return Result.Success(new MatchReport(
            ExactIdMappings: exactPairs.Count,
            StrongTitleYearMappings: strongMappings,
            TitleOnlyCandidates: titleOnlyHints,
            UnmatchedCandidates: unmatchedCandidates));
    }

    // ── Step 1: incremental Kometa fetch ────────────────────────────────────────

    private async Task<Result> RefreshKometaMappingsAsync(bool forceRefreshKometaMapping)
    {
        var etagEntry = await propertyBagRepository.GetByKeyAsync(SettingsKeys.KometaAnimeIdsJsonEtag);
        var lastModifiedEntry = await propertyBagRepository.GetByKeyAsync(SettingsKeys.KometaAnimeIdsJsonLastModified);
        var storedEtag = string.IsNullOrWhiteSpace(etagEntry?.Value) ? null : etagEntry.Value;
        var storedLastModified = string.IsNullOrWhiteSpace(lastModifiedEntry?.Value) ? null : lastModifiedEntry.Value;

        var hasMappingsResult = await kometaAnimeMappingService.AnyAsync();
        if (hasMappingsResult.IsFailure)
            return Result.Failure(hasMappingsResult.Error);

        // No local copy yet → always download the full document (conditional headers
        // could return 304 against validators while the table is empty).
        var force = forceRefreshKometaMapping || !hasMappingsResult.Value;

        var fetchResult = await kometaAnimeJsonService.GetIfModifiedAsync(storedEtag, storedLastModified, force);
        if (fetchResult.IsFailure)
            return Result.Failure(fetchResult.Error);

        var outcome = fetchResult.Value;
        if (outcome.NotModified)
        {
            // Remote document is unchanged — skip every local write (no wipe, no rebuild).
            return Result.Success();
        }

        var mergeResult = await kometaAnimeMappingService.MergeAsync(outcome.Items!.ToKometaAnimeMappings());
        if (mergeResult.IsFailure)
            return Result.Failure(mergeResult.Error);

        // Store the new validators only after the merge succeeded.
        await propertyBagRepository.UpsertAsync(SettingsKeys.KometaAnimeIdsJsonEtag, outcome.ETag ?? string.Empty);
        await propertyBagRepository.UpsertAsync(SettingsKeys.KometaAnimeIdsJsonLastModified, outcome.LastModified ?? string.Empty);
        return Result.Success();
    }

    // ── Matching helpers ────────────────────────────────────────────────────────

    private static Dictionary<(KometaAnimePlatform Platform, string PlatformId), List<KometaAnimeMapping>> BuildKometaIndex(
        IList<KometaAnimeMapping> kometaMappings)
    {
        var index = new Dictionary<(KometaAnimePlatform, string), List<KometaAnimeMapping>>();
        foreach (var km in kometaMappings)
        {
            var key = (km.GetPlatformEnum(), km.PlatformId);
            if (!index.TryGetValue(key, out var list))
                index[key] = list = [];
            list.Add(km);
        }

        return index;
    }

    /// <summary>
    /// Builds rich per-anime metadata (title variants, year, episode counts) from the
    /// provider cache blobs, falling back to the plain <see cref="Anime.Title"/>.
    /// </summary>
    private async Task<Dictionary<Guid, AnimeMeta>> BuildAnimeMetaAsync(List<Anime> allAnimes)
    {
        var cacheBySourceId = new Dictionary<(AnimeSource, string), CacheEntry>();
        var cacheResult = await cacheEntryService.GetAllAsync();
        if (cacheResult.IsSuccess)
        {
            foreach (var entry in cacheResult.Value)
                cacheBySourceId[(entry.AnimeSource, entry.SourceId)] = entry;
        }

        var result = new Dictionary<Guid, AnimeMeta>(allAnimes.Count);
        foreach (var anime in allAnimes)
        {
            var titles = new List<string> { anime.Title };
            int? year = null, episodes = null;

            if (cacheBySourceId.TryGetValue((anime.AnimeSource, anime.SourceId), out var entry))
            {
                try
                {
                    switch (anime.AnimeSource)
                    {
                        case AnimeSource.MyAnimeList:
                            var mal = JsonSerializer.Deserialize<MalAnime>(entry.RawData, CacheReadOptions);
                            if (mal is not null)
                            {
                                AddTitle(titles, mal.AlternativeTitles?.En);
                                AddTitle(titles, mal.AlternativeTitles?.Ja);
                                if (mal.AlternativeTitles?.Synonyms is { } synonyms)
                                    foreach (var synonym in synonyms) AddTitle(titles, synonym);
                                year = ParseYear(mal.StartDate) ?? mal.StartSeason?.Year;
                                episodes = mal.NumEpisodes > 0 ? mal.NumEpisodes : null;
                            }
                            break;

                        case AnimeSource.Sonarr:
                            var sonarr = JsonSerializer.Deserialize<SonarrSeries>(entry.RawData, CacheReadOptions);
                            if (sonarr is not null)
                            {
                                AddTitle(titles, sonarr.CleanTitle);
                                if (sonarr.AlternateTitles is { } alternates)
                                    foreach (var alternate in alternates) AddTitle(titles, alternate.Title);
                                year = sonarr.Year > 0 ? sonarr.Year
                                    : sonarr.FirstAired != default ? sonarr.FirstAired.Year : null;
                                episodes = sonarr.Statistics?.TotalEpisodeCount > 0 ? sonarr.Statistics.TotalEpisodeCount
                                    : sonarr.Statistics?.EpisodeCount > 0 ? sonarr.Statistics.EpisodeCount
                                    : null;
                            }
                            break;

                        case AnimeSource.Plex:
                            var plex = JsonSerializer.Deserialize<PlexMedia>(entry.RawData, CacheReadOptions);
                            if (plex is not null)
                            {
                                AddTitle(titles, plex.OriginalTitle);
                                year = plex.Year > 0 ? plex.Year : null;
                                episodes = plex.LeafCount > 0 ? plex.LeafCount : null;
                            }
                            break;
                    }
                }
                catch (JsonException)
                {
                    // Unreadable cache row — fall back to the plain title.
                }
            }

            // Base (season-stripped) forms for similarity: "One Piece Season 2" and
            // "One Piece" share the base "one piece"; franchise seasons are decided
            // explicitly via markers + tvdb_season, not by string distance.
            var normalizedTitles = titles
                .Select(t => TitleNormalizer.NormalizeWithSeason(t).Base)
                .Where(t => t.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            // The season marker comes from the main title only: alternate titles often carry
            // season-specific variants that would mislabel the base series.
            var seasonMarker = TitleNormalizer.ExtractSeasonMarker(anime.Title);

            result[anime.Id] = new AnimeMeta(normalizedTitles, year, episodes, seasonMarker);
        }

        return result;
    }

    private static void AddTitle(List<string> titles, string? title)
    {
        if (!string.IsNullOrWhiteSpace(title))
            titles.Add(title);
    }

    private static int? ParseYear(string? startDate)
        => !string.IsNullOrWhiteSpace(startDate) && startDate.Length >= 4
            && int.TryParse(startDate[..4], out var year)
            ? year
            : null;

    /// <summary>
    /// Symmetric best-case title similarity in [0, 1]: 1.0 for exact normalized equality,
    /// otherwise the minimum of both word-level fuzzy directions (kills containment
    /// false-positives such as "Title" vs "Title Season 2").
    /// A cheap word-overlap gate runs before the Levenshtein-heavy fuzzy scoring: a pair that
    /// would score ≥ 0.8 must share most words nearly verbatim, so pairs without enough exact
    /// word overlap can be skipped without touching the expensive path.
    /// </summary>
    private static double ScoreTitles(AnimeMeta a, AnimeMeta b)
    {
        double best = 0;
        foreach (var titleA in a.NormalizedTitles)
        {
            foreach (var titleB in b.NormalizedTitles)
            {
                if (string.Equals(titleA, titleB, StringComparison.Ordinal))
                    return 1.0;

                if (!HasEnoughWordOverlap(titleA, titleB))
                    continue;

                var similarity = Math.Min(
                    TitleSimilarity.ComputeWordLevelFuzzySimilarity(titleA, titleB),
                    TitleSimilarity.ComputeWordLevelFuzzySimilarity(titleB, titleA));
                if (similarity > best)
                    best = similarity;
            }
        }

        return best;
    }

    /// <summary>
    /// Cheap gate in front of fuzzy scoring: requires 1 shared exact word for short titles
    /// (≤ 3 words), 2 for longer ones. Pairs below that almost never reach the 0.8 candidate
    /// floor once fuzzy-scored (shared words dominate the per-word average), and this keeps
    /// the matching pass linear-ish in practice instead of O(U × R × words² × Levenshtein).
    /// </summary>
    private static bool HasEnoughWordOverlap(string titleA, string titleB)
    {
        var wordsA = titleA.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var wordsB = titleB.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (wordsA.Length == 0 || wordsB.Length == 0)
            return false;

        var required = Math.Min(wordsA.Length, wordsB.Length) <= 3 ? 1 : 2;

        // Compare on the smaller side's words against a set of the larger side's words.
        var (small, large) = wordsA.Length <= wordsB.Length ? (wordsA, wordsB) : (wordsB, wordsA);
        var largeSet = new HashSet<string>(large, StringComparer.Ordinal);

        var shared = small.Count(largeSet.Contains);
        return shared >= required;
    }

    /// <summary>
    /// Franchise-season disambiguation: decides whether <paramref name="candidateRows"/>'s
    /// AniDbIds may be borrowed and applies the Kometa <c>tvdb_season</c> split
    /// (rows with a <see cref="KometaAnimeMapping.Season"/>) to pick exactly the rows that
    /// belong to the unmatched row's season.
    /// </summary>
    private static bool TryResolveBridgeRows(
        AnimeMeta unresolvedMeta,
        AnimeMeta candidateMeta,
        List<KometaAnimeMapping> candidateRows,
        out List<string> aniDbIds)
    {
        aniDbIds = [];

        var unresolvedSeason = unresolvedMeta.SeasonMarker ?? 1;
        var candidateSeason = candidateMeta.SeasonMarker ?? 1;

        if (unresolvedSeason != candidateSeason)
        {
            // A later season may only bridge to an unmarked candidate when the candidate's
            // Kometa rows explicitly carve out that season via tvdb_season.
            if (candidateSeason == 1 && unresolvedSeason > 1)
            {
                var subset = candidateRows.Where(r => r.Season == unresolvedSeason).ToList();
                if (subset.Count == 0)
                    return false;

                aniDbIds = subset.Select(r => r.AniDbId).Distinct().ToList();
                return true;
            }

            return false;
        }

        if (unresolvedSeason > 1)
        {
            // Both rows are the same explicit season: prefer the rows Kometa assigns to it.
            var subset = candidateRows.Where(r => r.Season == unresolvedSeason || r.Season is null).ToList();
            aniDbIds = (subset.Count > 0 ? subset : candidateRows).Select(r => r.AniDbId).Distinct().ToList();
            return aniDbIds.Count > 0;
        }

        aniDbIds = candidateRows.Select(r => r.AniDbId).Distinct().ToList();
        return aniDbIds.Count > 0;
    }

    /// <summary>
    /// Picks the AniDbIds to bridge from the top-ranked strong candidates. Equally-ranked
    /// candidates are only ambiguous when they resolve to DISJOINT AniDbId sets — distinct
    /// shows that merely tie (e.g. a remake with an identical title) must stay unpersisted,
    /// while the same show seen through several platforms/ids (Plex + Sonarr rows sharing
    /// one AniDB identity) is safe to bridge. Shared identity ⇒ persist the union of the
    /// tied candidates' ids (matches the ID pass, which also attaches every row's ids).
    /// </summary>
    private static bool TrySelectBridge(List<ScoredCandidate> ranked, out List<string> aniDbIds)
    {
        aniDbIds = [];

        var first = ranked[0];
        var topGroup = ranked
            .Where(s => Math.Abs(s.Similarity - first.Similarity) < 0.01
                && s.YearDelta == first.YearDelta
                && s.EpisodeDelta == first.EpisodeDelta)
            .ToList();

        var sets = topGroup
            .Select(s => s.BorrowedAniDbIds.ToHashSet(StringComparer.Ordinal))
            .ToList();

        if (sets.Count > 1)
        {
            var shared = new HashSet<string>(sets[0], StringComparer.Ordinal);
            foreach (var set in sets.Skip(1))
                shared.IntersectWith(set);

            if (shared.Count == 0)
                return false; // genuinely distinct shows in a tie — report, never persist
        }

        aniDbIds = sets.SelectMany(s => s).Distinct(StringComparer.Ordinal).ToList();
        return aniDbIds.Count > 0;
    }

    private static Task NotifyAsync(
        ISyncProgressNotifier? notifier, string stepKey, SyncStepState state,
        int? progressPercent, Stopwatch sw, string? errorMessage = null)
    {
        if (notifier is null) return Task.CompletedTask;
        return notifier.NotifyAsync(new SyncProgressUpdate(stepKey, state, progressPercent, sw.ElapsedMilliseconds, errorMessage));
    }
}
