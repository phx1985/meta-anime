using System.Globalization;
using MetaAnime.Application.Services.AniList;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Interfaces.Shared.Results;
using MetaAnime.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace MetaAnime.Application.Services;

/// <summary>Outcome of a successful cast sync.</summary>
public enum CastFetchOutcome
{
    /// <summary>Cast rows already existed locally.</summary>
    AlreadyCached,
    /// <summary>AniList responded and rows were persisted.</summary>
    Stored,
    /// <summary>AniList definitively has no cast for this anime.</summary>
    NoData,
}

/// <summary>
/// Lazy, DB-first cast sync from the AniList GraphQL API — no authentication required.
/// Existing rows are served with zero network calls (even while AniList is down); misses go
/// through the pacing queue and are remembered in a two-state negative cache:
/// ProviderUnavailable → immediate 503, DefinitiveEmpty → immediate empty graph.
/// The cast query is paged (25 character edges per page) and looped until
/// <c>pageInfo.hasNextPage</c> is false, bounded at 10 pages, so the FULL cast is fetched.
/// </summary>
public sealed class AniListCastService(
    IAnimeCastRepository animeCastRepository,
    IAniListClient anilistClient,
    AniListCastNegativeCache negativeCache,
    CastIdSpaceMigrator idSpaceMigrator,
    ILogger<AniListCastService> logger)
{
    /// <summary>25 character edges per GraphQL page; hard bound keeps a pathological page loop finite.</summary>
    private const int MaxPages = 10;

    /// <summary>
    /// Returns immediately if cast rows already exist for <paramref name="malId"/> (DB-first).
    /// Otherwise checks the negative cache, then lazily fetches the full paged cast from AniList
    /// and bulk-upserts the rows transactionally. Fails only when the provider is unavailable
    /// and nothing is cached.
    /// </summary>
    public async Task<Result<CastFetchOutcome>> EnsureAsync(string malId)
    {
        try
        {
            // Stale MAL-ID rows would masquerade as cache hits — clear the old ID space first.
            var migrated = await idSpaceMigrator.EnsureMigratedAsync();
            if (migrated.IsFailure)
                return Result.Failure<CastFetchOutcome>(migrated.Error);

            // 1) DB-first: rows present → zero network, serves even while the provider is down.
            var existing = (await animeCastRepository.GetByAnimeMalIdAsync(malId)).ToList();
            if (existing.Count > 0)
            {
                negativeCache.Clear(malId);
                negativeCache.ClearLoading(malId);
                return Result.Success(CastFetchOutcome.AlreadyCached);
            }

            // 2) Negative cache on miss: both states answer with zero network.
            if (negativeCache.TryGet(malId, out var negative))
            {
                return negative switch
                {
                    AniListNegativeState.DefinitiveEmpty => Result.Success(CastFetchOutcome.NoData),
                    _ => Result.Failure<CastFetchOutcome>(ProviderUnavailable(malId)),
                };
            }

            // 3) Lazy fetch through the pacing queue (all pages of the cast).
            logger.LogInformation(Constants.LogMessages.CastSyncJob.Starting, malId);
            negativeCache.MarkLoading(malId);
            FetchAllPagesResult fetched;
            try
            {
                fetched = await FetchAllPagesAsync(malId);
            }
            finally
            {
                negativeCache.ClearLoading(malId);
            }

            if (fetched.Status == AniListApiStatus.Unavailable)
            {
                logger.LogWarning(Constants.LogMessages.CastSyncJob.Error, malId);
                negativeCache.Set(malId, AniListNegativeState.ProviderUnavailable);
                return Result.Failure<CastFetchOutcome>(ProviderUnavailable(malId));
            }

            if (fetched.Rows.Count == 0)
            {
                // AniList has no cast for this anime (or an unknown idMal → data.Media == null):
                // a definitive answer, remembered briefly.
                logger.LogInformation(Constants.LogMessages.CastSyncJob.Skipped, malId);
                negativeCache.Set(malId, AniListNegativeState.DefinitiveEmpty);
                return Result.Success(CastFetchOutcome.NoData);
            }

            // 4) Persist (repository bulk-upsert = single transactional SaveChanges).
            await animeCastRepository.BulkUpsertAsync(fetched.Rows);
            logger.LogInformation(Constants.LogMessages.CastSyncJob.Succeeded, malId, fetched.Rows.Count);
            negativeCache.Clear(malId);
            return Result.Success(CastFetchOutcome.Stored);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, Constants.LogMessages.CastSyncJob.Error, malId);
            return Result.Failure<CastFetchOutcome>(new(Constants.Errors.SyncAnimeCastAsync, ex.Message));
        }
    }

    /// <summary>The exact 503 contract message for an unavailable cast provider.</summary>
    private static Error ProviderUnavailable(string malId) =>
        new(Constants.Errors.CastProviderUnavailable, $"Cast provider is unavailable for anime {malId}");

    private sealed record FetchAllPagesResult(AniListApiStatus Status, List<AnimeCast> Rows);

    /// <summary>
    /// Loops the cast query page by page while <c>pageInfo.hasNextPage</c> says there is more,
    /// bounded at <see cref="MaxPages"/> pages (250 character entries). Any Unavailable page
    /// aborts the whole fetch unpersisted — a partial cast stored as complete would poison the
    /// DB-first cache. An unknown idMal (<c>data.Media == null</c>) is a definitive empty.
    /// </summary>
    private async Task<FetchAllPagesResult> FetchAllPagesAsync(string malId)
    {
        if (!int.TryParse(malId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var malIdNumber))
            return new(AniListApiStatus.Success, []); // non-numeric MAL id can never exist upstream

        var rows = new List<AnimeCast>();

        for (var page = 1; page <= MaxPages; page++)
        {
            var result = await anilistClient.PostQueryAsync<AniListMediaCastData>(
                AniListQueries.AnimeCast,
                new Dictionary<string, object?> { ["malId"] = malIdNumber, ["page"] = page });

            if (result.Status == AniListApiStatus.Unavailable)
                return new(AniListApiStatus.Unavailable, []);

            // Unknown idMal (or unknown page): data.Media == null is a definitive answer.
            var characters = result.Data?.Media?.Characters;
            if (characters is null)
                return new(AniListApiStatus.Success, []);

            rows.AddRange(MapEdgesToCastRows(malId, characters.Edges));

            if (characters.PageInfo?.HasNextPage != true)
                break; // full cast fetched (or the 10-page bound stops a pathological loop)
        }

        return new(AniListApiStatus.Success, rows);
    }

    private static IEnumerable<AnimeCast> MapEdgesToCastRows(string malId, List<AniListCharacterEdge>? edges)
    {
        var now = DateTime.UtcNow;
        return (edges ?? [])
            .Where(e => e.Node is { Id: > 0 })
            .SelectMany(e => (e.VoiceActors ?? [])
                .Where(va => va.Id > 0)
                .Select(va => new AnimeCast
                {
                    AnimeMalId        = malId,
                    CharacterId       = e.Node!.Id,
                    CharacterName     = e.Node.Name?.Full ?? string.Empty,
                    CharacterRole     = MapRole(e.Role),
                    CharacterImageUrl = e.Node.Image?.GetImageUrl(),
                    VaPersonId        = va.Id,
                    VaName            = va.Name?.Full ?? string.Empty,
                    VaImageUrl        = va.Image?.GetImageUrl(),
                    Language          = va.LanguageV2 ?? string.Empty,
                    SyncedAt          = now,
                }))
            // AniList returns multiple VAs per character+language (most relevant first) — keep one row per key.
            .GroupBy(x => (x.CharacterId, x.Language))
            .Select(g => g.First())
            .ToList();
    }

    /// <summary>AniList role enum → the role strings the REST contract emits.</summary>
    private static string MapRole(string? role) => role switch
    {
        "MAIN"       => "Main",
        "SUPPORTING" => "Supporting",
        "BACKGROUND" => "Supporting",
        _            => role ?? string.Empty,
    };
}
