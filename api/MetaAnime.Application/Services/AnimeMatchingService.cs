using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Interfaces.Shared.Results;
using MetaAnime.Core.Sync;
using MetaAnime.Domain.Entities;
using MetaAnime.Domain.Enums;
using MetaAnime.Infrastructure.Kometa.Models;
using MetaAnime.Infrastructure.Kometa.Utilities.Extensions;
using MetaAnime.Infrastructure.Utilities.Extensions;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace MetaAnime.Application.Services;

public sealed class AnimeMatchingService(
    IKometaAnimeJsonService<KometaRaw> kometaAnimeJsonService,
    IKometaAnimeMappingService kometaAnimeMappingService,
    IAnimeMappingService mappingService,
    IAnimeService animeService)
    : IAnimeMatchingService
{
    public async Task<Result> MatchAsync(bool forceRefreshKometaMapping = false, ISyncProgressNotifier? progressNotifier = null)
    {
        var sw = Stopwatch.StartNew();

        // ── Step 1: Kometa mappings ──────────────────────────────────────────
        await NotifyAsync(progressNotifier, SyncStepKeys.Kometa, SyncStepState.Running, 0, sw);

        if (forceRefreshKometaMapping)
        {
            var deleteKometaMappingsResult = await kometaAnimeMappingService.BulkDeleteAllAsync();
            if (deleteKometaMappingsResult.IsFailure)
            {
                await NotifyAsync(progressNotifier, SyncStepKeys.Kometa, SyncStepState.Error, null, sw, deleteKometaMappingsResult.Error.Message);
                return Result.Failure(deleteKometaMappingsResult.Error);
            }
        }

        var hasMappingsResult = forceRefreshKometaMapping
            ? Result.Success(false)
            : await kometaAnimeMappingService.AnyAsync();
        if (hasMappingsResult.IsFailure)
        {
            await NotifyAsync(progressNotifier, SyncStepKeys.Kometa, SyncStepState.Error, null, sw, hasMappingsResult.Error.Message);
            return Result.Failure(hasMappingsResult.Error);
        }

        if (!hasMappingsResult.Value)
        {
            var rawMappingsResult = await kometaAnimeJsonService.GetAllAsync();
            if (rawMappingsResult.IsFailure)
            {
                await NotifyAsync(progressNotifier, SyncStepKeys.Kometa, SyncStepState.Error, null, sw, rawMappingsResult.Error.Message);
                return Result.Failure(rawMappingsResult.Error);
            }

            var addKometaMappingsResult = await kometaAnimeMappingService.BulkInsertAsync(rawMappingsResult.Value.ToKometaAnimeMappings());
            if (addKometaMappingsResult.IsFailure)
            {
                await NotifyAsync(progressNotifier, SyncStepKeys.Kometa, SyncStepState.Error, null, sw, addKometaMappingsResult.Error.Message);
                return Result.Failure(addKometaMappingsResult.Error);
            }
        }

        await NotifyAsync(progressNotifier, SyncStepKeys.Kometa, SyncStepState.Done, 100, sw);
        sw.Restart();

        // ── Step 2: Fetch from sources (Plex → MAL → Sonarr sequentially) ─────
        // Per-source Running/Done/Error notifications are emitted inside RefreshData
        // so the UI sees each provider progress individually.
        var refreshDataResult = await animeService.RefreshData(progressNotifier);
        if (refreshDataResult.IsFailure)
        {
            return Result.Failure(refreshDataResult.Error);
        }

        sw.Restart();

        // ── Step 3: Match anime across sources ───────────────────────────────
        await NotifyAsync(progressNotifier, SyncStepKeys.Match, SyncStepState.Running, 0, sw);

        var kometaMappingsResult = await kometaAnimeMappingService.GetAllAsync();
        if (kometaMappingsResult.IsFailure)
        {
            await NotifyAsync(progressNotifier, SyncStepKeys.Match, SyncStepState.Error, null, sw, kometaMappingsResult.Error.Message);
            return Result.Failure(kometaMappingsResult.Error);
        }

        var kometaMappings = kometaMappingsResult.Value;
        var allAnimes = refreshDataResult.Value;

        var deleteMappingsResult = await mappingService.BulkDeleteAllAsync();
        if (deleteMappingsResult.IsFailure)
        {
            await NotifyAsync(progressNotifier, SyncStepKeys.Match, SyncStepState.Error, null, sw, deleteMappingsResult.Error.Message);
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
            await NotifyAsync(progressNotifier, SyncStepKeys.Match, SyncStepState.Error, null, sw, addMappingsResult.Error.Message);
            return Result.Failure(addMappingsResult.Error);
        }

        await NotifyAsync(progressNotifier, SyncStepKeys.Match, SyncStepState.Done, 100, sw);
        return Result.Success();
    }

    private static Task NotifyAsync(
        ISyncProgressNotifier? notifier, string stepKey, SyncStepState state,
        int? progressPercent, Stopwatch sw, string? errorMessage = null)
    {
        if (notifier is null) return Task.CompletedTask;
        return notifier.NotifyAsync(new SyncProgressUpdate(stepKey, state, progressPercent, sw.ElapsedMilliseconds, errorMessage));
    }
}
