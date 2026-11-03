using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Interfaces.Shared.Results;
using Microsoft.Extensions.Logging;

namespace MetaAnime.Application.Services;

/// <summary>
/// One-time guard against mixing ID spaces in <c>AnimeCast</c>/<c>VoiceActor</c>. Rows written
/// before the AniList migration carry MAL person/character IDs from the dead Jikan era; mixing
/// them with AniList IDs corrupts appearsIn/profile lookups and makes stale rows act as cache
/// hits. When the <c>PropertyBag</c> marker <c>CastIdSpace</c> is not <c>anilist-v1</c>, ALL
/// rows in both tables are deleted and the marker is written. Idempotent, schema-unchanged:
/// once the marker is set every later run is a no-op read of one row.
/// </summary>
public sealed class CastIdSpaceMigrator(
    IAnimeCastRepository animeCastRepository,
    IVoiceActorRepository voiceActorRepository,
    IPropertyBagRepository propertyBagRepository,
    ILogger<CastIdSpaceMigrator> logger)
{
    public const string MarkerKey = "CastIdSpace";
    public const string AniListIdSpace = "anilist-v1";

    /// <summary>
    /// Ensures the cast/VA tables hold only AniList-ID rows. Safe to call on every access —
    /// reads the marker first and does nothing once the migration has run.
    /// </summary>
    public async Task<Result> EnsureMigratedAsync()
    {
        try
        {
            var marker = await propertyBagRepository.GetByKeyAsync(MarkerKey);
            if (marker?.Value == AniListIdSpace)
                return Result.Success();

            logger.LogWarning(
                "Cast ID space marker is '{Current}' — clearing AnimeCast/VoiceActor rows written in the old MAL ID space",
                marker?.Value ?? "<missing>");

            await animeCastRepository.DeleteAllAsync();
            await voiceActorRepository.DeleteAllAsync();
            await propertyBagRepository.UpsertAsync(MarkerKey, AniListIdSpace);

            logger.LogInformation("Cast ID space migrated to {IdSpace}", AniListIdSpace);
            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cast ID space migration failed");
            return Result.Failure(new(Constants.Errors.MigrateCastIdSpace, ex.Message));
        }
    }
}
