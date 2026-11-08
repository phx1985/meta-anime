using Mediator;
using MetaAnime.Application.Contracts;
using MetaAnime.Core;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Queries;

public sealed class GetSyncStatusQueryHandler(
    IPropertyBagRepository propertyBagRepository,
    ISyncStateTracker stateTracker)
    : IRequestHandler<GetSyncStatusQuery, Result<SyncStatusResponse>>
{
    public async ValueTask<Result<SyncStatusResponse>> Handle(GetSyncStatusQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var entry = await propertyBagRepository.GetByKeyAsync(SettingsKeys.LastSyncDate);

            DateTimeOffset? last = null;
            if (!string.IsNullOrWhiteSpace(entry?.Value) && DateTimeOffset.TryParse(entry.Value, out var parsed))
                last = parsed;

            // The persisted date is authoritative for "last completed"; the tracker contributes
            // the live running flags so a page refresh can restore an in-flight state.
            return Result.Success(new SyncStatusResponse(
                last ?? stateTracker.LastSyncDate,
                IsSyncRunning: stateTracker.IsFullSyncRunning,
                IsActivitySyncRunning: stateTracker.IsActivitySyncRunning,
                LastActivitySyncDate: stateTracker.LastActivitySyncDate));
        }
        catch (Exception ex)
        {
            return Result.Failure<SyncStatusResponse>(new(Constants.Errors.GetSyncStatus, ex.Message));
        }
    }
}
