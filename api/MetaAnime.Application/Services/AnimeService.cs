using MetaAnime.Core;
using MetaAnime.Core.Interfaces.Caching;
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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;

namespace MetaAnime.Application.Services;

public sealed class AnimeService(
    IAnimeRepository animeRepository,
    ICacheEntryService cacheEntryService,
    IPropertyBagRepository propertyBagRepository,
    IServiceScopeFactory scopeFactory,
    ILogger<AnimeService> logger) : IAnimeService
{
    private static readonly JsonSerializerOptions CacheReadOptions = new() { PropertyNameCaseInsensitive = true };

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

            // ── Fetch all enabled providers concurrently ──────────────────────────
            // Each provider runs in its own DI scope: the provider services resolve a scoped
            // DbContext through IPropertyBagRepository, and EF Core forbids concurrent use of
            // one context instance. Separate scopes give each provider its own DbContext so the
            // network-bound fetches can safely overlap. Each step emits its own
            // Running/Done/Error SignalR messages so the UI tracks per-provider progress.
            var malTask = FetchProviderAsync(
                SyncStepKeys.Mal,
                progressNotifier,
                scope => scope.ServiceProvider.GetRequiredService<IMalAnimeSourceService<MalAnime>>().GetAllAsync());

            var plexTask = plexEnabled
                ? FetchProviderAsync(
                    SyncStepKeys.Plex,
                    progressNotifier,
                    scope => scope.ServiceProvider.GetRequiredService<IPlexAnimeSourceService<PlexMedia>>().GetAllAsync())
                : Task.FromResult(Result.Success<IList<PlexMedia>>([]));

            var sonarrTask = sonarrEnabled
                ? FetchProviderAsync(
                    SyncStepKeys.Sonarr,
                    progressNotifier,
                    scope => scope.ServiceProvider.GetRequiredService<ISonarrAnimeSourceService<SonarrSeries>>().GetAllAsync())
                : Task.FromResult(Result.Success<IList<SonarrSeries>>([]));

            await Task.WhenAll(malTask, plexTask, sonarrTask);

            var malResult = await malTask;
            var plexResult = await plexTask;
            var sonarrResult = await sonarrTask;

            // Preserve existing semantics: the first failing provider aborts the whole
            // refresh before anything is persisted (MAL → Plex → Sonarr priority).
            if (malResult.IsFailure)
                return Result.Failure<IEnumerable<Anime>>(malResult.Error);
            if (plexResult.IsFailure)
                return Result.Failure<IEnumerable<Anime>>(plexResult.Error);
            if (sonarrResult.IsFailure)
                return Result.Failure<IEnumerable<Anime>>(sonarrResult.Error);

            // ── Persist all sources with diff-based merges ────────────────────────
            // Unchanged rows are never rewritten; rows missing from a fetched source are
            // tombstoned. Disabled providers merge an empty set (matching the old
            // wipe-and-rebuild semantics where their rows disappeared on sync).
            await CarryOverSonarrCodecsAsync(sonarrResult.Value);

            await MergeSourceAsync(AnimeSource.MyAnimeList, malResult.Value);
            await MergeSourceAsync(AnimeSource.Plex, plexEnabled ? plexResult.Value : []);
            await MergeSourceAsync(AnimeSource.Sonarr, sonarrEnabled ? sonarrResult.Value : []);

            var freshAnimes = await animeRepository.GetAllAsync();
            return Result.Success(freshAnimes);
        }
        catch (Exception ex)
        {
            return Result.Failure<IEnumerable<Anime>>(new(Constants.Errors.RefreshData, ex.Message));
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fetches one provider inside its own DI scope and emits per-provider progress.
    /// Failures are returned as <see cref="Result{T}"/> errors (no exceptions escape).
    /// </summary>
    private async Task<Result<IList<T>>> FetchProviderAsync<T>(
        string stepKey,
        ISyncProgressNotifier? notifier,
        Func<IServiceScope, Task<Result<IList<T>>>> fetch)
    {
        var sw = Stopwatch.StartNew();
        await NotifyAsync(notifier, stepKey, SyncStepState.Running, 0, sw);

        try
        {
            using var scope = scopeFactory.CreateScope();
            var result = await fetch(scope);
            if (result.IsFailure)
            {
                logger.LogError("{Step} sync failed: {ErrorCode} — {ErrorMessage}", stepKey, result.Error.Code, result.Error.Message);
                await NotifyAsync(notifier, stepKey, SyncStepState.Error, null, sw, result.Error.Message);
                return result;
            }

            logger.LogInformation("{Step} sync fetched {Count} items in {ElapsedMs}ms", stepKey, result.Value.Count, sw.ElapsedMilliseconds);
            await NotifyAsync(notifier, stepKey, SyncStepState.Done, 100, sw);
            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "{Step} sync threw", stepKey);
            await NotifyAsync(notifier, stepKey, SyncStepState.Error, null, sw, ex.Message);
            return Result.Failure<IList<T>>(new(Constants.Errors.RefreshData, ex.Message));
        }
    }

    private async Task MergeSourceAsync<T>(AnimeSource source, IEnumerable<T> items)
        where T : class, IAnimeConvertible, ICacheable
    {
        var list = items.ToList();
        await animeRepository.MergeBySourceAsync(source, list.Select(i => i.ToAnime()));
        var cacheResult = await cacheEntryService.MergeBySourceAsync(source, list);
        if (cacheResult.IsFailure)
            logger.LogError("Cache merge failed for {Source}: {Error}", source, cacheResult.Error.Message);
    }

    /// <summary>
    /// Carries the cached codec distribution over to Sonarr series whose files did not change
    /// (keyed by series id + <c>added</c> + statistics). Series that changed lose their cached
    /// codec and are picked up by the asynchronous codec enrichment job — so codec fetching
    /// never blocks the sync critical path.
    /// </summary>
    private async Task CarryOverSonarrCodecsAsync(IList<SonarrSeries> seriesList)
    {
        if (seriesList.Count == 0)
            return;

        var existingResult = await cacheEntryService.GetAsync(AnimeSource.Sonarr);
        if (existingResult.IsFailure)
            return;

        var previousById = new Dictionary<string, SonarrSeries>(StringComparer.Ordinal);
        foreach (var entry in existingResult.Value)
        {
            try
            {
                var previous = JsonSerializer.Deserialize<SonarrSeries>(entry.RawData, CacheReadOptions);
                if (previous is not null)
                    previousById[entry.SourceId] = previous;
            }
            catch (JsonException)
            {
                // Unreadable cache row — the enrichment job will rebuild it.
            }
        }

        foreach (var series in seriesList)
        {
            if (series.CodecDistribution.Count > 0)
                continue;

            if (!previousById.TryGetValue(series.Id.ToString(), out var previous))
                continue;

            if (previous.CodecDistribution is not { Count: > 0 })
                continue;

            if (previous.Added != series.Added)
                continue;

            if (!SameFileStatistics(previous.Statistics, series.Statistics))
                continue;

            series.CodecDistribution = previous.CodecDistribution;
        }
    }

    private static bool SameFileStatistics(Statistics? previous, Statistics? current)
    {
        if (previous is null || current is null)
            return previous is null && current is null;

        return previous.EpisodeFileCount == current.EpisodeFileCount
            && previous.TotalEpisodeCount == current.TotalEpisodeCount
            && string.Equals(previous.SizeOnDisk?.ToString(), current.SizeOnDisk?.ToString(), StringComparison.Ordinal);
    }

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
