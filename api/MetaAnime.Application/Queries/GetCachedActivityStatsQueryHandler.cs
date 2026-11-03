using Mediator;
using MetaAnime.Application.Contracts;
using MetaAnime.Core;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Interfaces.Shared.Results;
using System.Text.Json;

namespace MetaAnime.Application.Queries;

public sealed class GetCachedActivityStatsQueryHandler(IPropertyBagRepository propertyBagRepository)
    : IRequestHandler<GetCachedActivityStatsQuery, Result<ActivityStatsResponse?>>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async ValueTask<Result<ActivityStatsResponse?>> Handle(
        GetCachedActivityStatsQuery request,
        CancellationToken cancellationToken)
    {
        try
        {
            // The snapshot is pre-aggregated at UTC. Serving it to a caller in another timezone
            // would put downloads on the wrong day (up to a whole day of skew), so those
            // callers fall through to the DB-backed computation, which is equally cheap.
            if (request.TzOffsetMinutes != 0)
                return Result.Success<ActivityStatsResponse?>(null);

            var cacheEntry       = await propertyBagRepository.GetByKeyAsync(SettingsKeys.DownloadStatsCache);
            var lastUpdatedEntry = await propertyBagRepository.GetByKeyAsync(SettingsKeys.DownloadStatsCacheLastUpdated);

            if (cacheEntry is null || string.IsNullOrWhiteSpace(cacheEntry.Value))
                return Result.Success<ActivityStatsResponse?>(null);

            var stats = JsonSerializer.Deserialize<ActivityStatsResponse>(cacheEntry.Value, JsonOptions);
            if (stats is null)
                return Result.Success<ActivityStatsResponse?>(null);

            var lastUpdated = lastUpdatedEntry?.Value;
            return Result.Success<ActivityStatsResponse?>(stats with { LastUpdated = lastUpdated });
        }
        catch (Exception ex)
        {
            return Result.Failure<ActivityStatsResponse?>(new(Constants.Errors.GetCachedActivityStatsAsync, ex.Message));
        }
    }
}
