using MetaAnime.Core;
using MetaAnime.Core.Interfaces.Mappers;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Interfaces.Shared.Results;
using MetaAnime.Core.Sync;
using MetaAnime.Domain.Entities;
using MetaAnime.Domain.Enums;
using MetaAnime.Infrastructure.MyAnimeList.Models;
using MetaAnime.Infrastructure.Plex.Models;
using MetaAnime.Infrastructure.SonarrAPIs.Sonarr.Models;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace MetaAnime.Application.Services;

public sealed class AnimeService(
    IAnimeRepository animeRepository,
    IPlexAnimeSourceService<PlexMedia> plexAnimeSourceService,
    IMalAnimeSourceService<MalAnime> malAnimeSourceService,
    ISonarrAnimeSourceService<SonarrSeries> sonarrAnimeSourceService,
    ICacheEntryService cacheEntryService,
    IPropertyBagRepository propertyBagRepository,
    ILogger<AnimeService> logger) : IAnimeService
{
    public async Task<Result> BulkInsertAsync(IEnumerable<Anime> animes)
    {
        try
        {
            await animeRepository.BulkInsertAsync(animes);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(new(Constants.Errors.BulkInsertAsync, ex.Message));
        }
    }

    public async Task<Result> BulkInsertAsync(IEnumerable<IAnimeConvertible> animeConvertibles)
    {
        try
        {
            await animeRepository.BulkInsertAsync(animeConvertibles);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(new(Constants.Errors.BulkInsertAsync, ex.Message));
        }
    }

    public async Task<Result> BulkDeleteAllAsync()
    {
        try
        {
            await animeRepository.BulkDeleteAllAsync();
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(new(Constants.Errors.BulkDeleteAllAsync, ex.Message));
        }
    }

    public async Task<Result> BulkDeleteBySourceAsync(AnimeSource animeSource)
    {
        try
        {
            await animeRepository.BulkDeleteBySourceAsync(animeSource);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(new(Constants.Errors.BulkDeleteBySourceAsync, ex.Message));
        }
    }

    public async Task<Result<IEnumerable<Anime>>> GetAllAsync()
    {
        try
        {
            var animes = await animeRepository.GetAllAsync();
            return Result.Success(animes);
        }
        catch (Exception ex)
        {
            return Result.Failure<IEnumerable<Anime>>(new(Constants.Errors.GetAllAsync, ex.Message));
        }
    }

    public async Task<Result<IEnumerable<Anime>>> GetAllBySourceAsync(AnimeSource source)
    {
        try
        {
            var animes = await animeRepository.GetAllBySourceAsync(source);
            return Result.Success(animes);
        }
        catch (Exception ex)
        {
            return Result.Failure<IEnumerable<Anime>>(new(Constants.Errors.GetAllBySourceAsync, ex.Message));
        }
    }

    public async Task<Result<Anime?>> GetAnimeAsync(AnimeSource source, string sourceId)
    {
        try
        {
            var anime = await animeRepository.GetAnimeAsync(source, sourceId);
            return Result.Success(anime);
        }
        catch (Exception ex)
        {
            return Result.Failure<Anime?>(new(Constants.Errors.GetAnimeAsync, ex.Message));
        }
    }

    public async Task<Result<IEnumerable<Anime>>> RefreshData(ISyncProgressNotifier? progressNotifier = null)
    {
        try
        {
            // ── Read provider enabled flags ───────────────────────────────────────
            var plexEnabledEntry = await propertyBagRepository.GetByKeyAsync(SettingsKeys.PlexEnabled);
            var sonarrEnabledEntry = await propertyBagRepository.GetByKeyAsync(SettingsKeys.SonarrEnabled);
            var plexEnabled = plexEnabledEntry?.Value == "true";
            var sonarrEnabled = sonarrEnabledEntry?.Value == "true";

            // Each source runs sequentially and emits its own Running/Done/Error
            // SignalR messages so the UI can track per-provider progress.
            var sw = Stopwatch.StartNew();

            // ── Step: MAL ────────────────────────────────────────────────────────
            await NotifyAsync(progressNotifier, SyncStepKeys.Mal, SyncStepState.Running, 0, sw);
            var malResult = await malAnimeSourceService.GetAllAsync();
            if (malResult.IsFailure)
            {
                logger.LogError("MAL sync failed: {ErrorCode} — {ErrorMessage}", malResult.Error.Code, malResult.Error.Message);
                await NotifyAsync(progressNotifier, SyncStepKeys.Mal, SyncStepState.Error, null, sw, malResult.Error.Message);
                return Result.Failure<IEnumerable<Anime>>(malResult.Error);
            }
            logger.LogInformation("MAL sync fetched {Count} anime in {ElapsedMs}ms", malResult.Value.Count, sw.ElapsedMilliseconds);
            await NotifyAsync(progressNotifier, SyncStepKeys.Mal, SyncStepState.Done, 100, sw);
            sw.Restart();

            // ── Step: Plex ───────────────────────────────────────────────────────
            IList<PlexMedia> plexAnimes = [];
            if (plexEnabled)
            {
                await NotifyAsync(progressNotifier, SyncStepKeys.Plex, SyncStepState.Running, 0, sw);
                var plexResult = await plexAnimeSourceService.GetAllAsync();
                if (plexResult.IsFailure)
                {
                    logger.LogError("Plex sync failed: {ErrorCode} — {ErrorMessage}", plexResult.Error.Code, plexResult.Error.Message);
                    await NotifyAsync(progressNotifier, SyncStepKeys.Plex, SyncStepState.Error, null, sw, plexResult.Error.Message);
                    return Result.Failure<IEnumerable<Anime>>(plexResult.Error);
                }
                plexAnimes = plexResult.Value;
                logger.LogInformation("Plex sync fetched {Count} anime in {ElapsedMs}ms", plexAnimes.Count, sw.ElapsedMilliseconds);
                await NotifyAsync(progressNotifier, SyncStepKeys.Plex, SyncStepState.Done, 100, sw);
                sw.Restart();
            }

            // ── Step: Sonarr ─────────────────────────────────────────────────────
            IList<SonarrSeries> sonarrAnimes = [];
            if (sonarrEnabled)
            {
                await NotifyAsync(progressNotifier, SyncStepKeys.Sonarr, SyncStepState.Running, 0, sw);
                var sonarrResult = await sonarrAnimeSourceService.GetAllAsync();
                if (sonarrResult.IsFailure)
                {
                    logger.LogError("Sonarr sync failed: {ErrorCode} — {ErrorMessage}", sonarrResult.Error.Code, sonarrResult.Error.Message);
                    await NotifyAsync(progressNotifier, SyncStepKeys.Sonarr, SyncStepState.Error, null, sw, sonarrResult.Error.Message);
                    return Result.Failure<IEnumerable<Anime>>(sonarrResult.Error);
                }

                // Enrich each Sonarr series with codec distribution so it is included in the cache.
                // Settings are fetched once (avoiding concurrent DbContext access); the HTTP calls
                // to Sonarr run in parallel inside GetBulkCodecDistributionsAsync.
                var codecDistributions = await sonarrAnimeSourceService.GetBulkCodecDistributionsAsync(
                    sonarrResult.Value.Select(s => s.Id.ToString()));
                foreach (var series in sonarrResult.Value)
                {
                    if (codecDistributions.TryGetValue(series.Id.ToString(), out var dist))
                        series.CodecDistribution = dist;
                }
                sonarrAnimes = sonarrResult.Value;
                logger.LogInformation("Sonarr sync fetched {Count} series in {ElapsedMs}ms", sonarrAnimes.Count, sw.ElapsedMilliseconds);
                await NotifyAsync(progressNotifier, SyncStepKeys.Sonarr, SyncStepState.Done, 100, sw);
            }

            // ── Persist all sources to DB + cache ────────────────────────────────
            await animeRepository.BulkDeleteAllAsync();
            await animeRepository.BulkInsertAsync([.. plexAnimes, .. malResult.Value, .. sonarrAnimes]);

            var deletePlexResult = await cacheEntryService.BulkDeleteBySourceAsync(AnimeSource.Plex);
            if (deletePlexResult.IsFailure)
                return Result.Failure<IEnumerable<Anime>>(deletePlexResult.Error);

            var deleteMalResult = await cacheEntryService.BulkDeleteBySourceAsync(AnimeSource.MyAnimeList);
            if (deleteMalResult.IsFailure)
                return Result.Failure<IEnumerable<Anime>>(deleteMalResult.Error);

            var deleteSonarrResult = await cacheEntryService.BulkDeleteBySourceAsync(AnimeSource.Sonarr);
            if (deleteSonarrResult.IsFailure)
                return Result.Failure<IEnumerable<Anime>>(deleteSonarrResult.Error);

            var addPlexResult = await cacheEntryService.BulkInsertAsync(plexAnimes);
            if (addPlexResult.IsFailure)
                return Result.Failure<IEnumerable<Anime>>(addPlexResult.Error);

            var addMalResult = await cacheEntryService.BulkInsertAsync(malResult.Value);
            if (addMalResult.IsFailure)
                return Result.Failure<IEnumerable<Anime>>(addMalResult.Error);

            var addSonarrResult = await cacheEntryService.BulkInsertAsync(sonarrAnimes);
            if (addSonarrResult.IsFailure)
                return Result.Failure<IEnumerable<Anime>>(addSonarrResult.Error);

            var freshAnimes = await animeRepository.GetAllAsync();
            return Result.Success(freshAnimes);
        }
        catch (Exception ex)
        {
            return Result.Failure<IEnumerable<Anime>>(new(Constants.Errors.RefreshData, ex.Message));
        }
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private static Task NotifyAsync(
        ISyncProgressNotifier? notifier, string stepKey, SyncStepState state,
        int? progressPercent, Stopwatch sw, string? errorMessage = null)
    {
        if (notifier is null) return Task.CompletedTask;
        return notifier.NotifyAsync(new SyncProgressUpdate(stepKey, state, progressPercent, sw.ElapsedMilliseconds, errorMessage));
    }

    public async Task<Result<Anime?>> GetByIdAsync(Guid id)
    {
        try
        {
            var anime = await animeRepository.GetByIdAsync(id);
            return Result.Success(anime);
        }
        catch (Exception ex)
        {
            return Result.Failure<Anime?>(new(Constants.Errors.GetByIdAsync, ex.Message));
        }
    }

    public async Task<Result> DeleteAsync(Guid id)
    {
        try
        {
            await animeRepository.DeleteAsync(id);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(new(Constants.Errors.DeleteAsync, ex.Message));
        }
    }
}
