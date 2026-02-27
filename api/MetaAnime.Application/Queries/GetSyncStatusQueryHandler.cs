using MediatR;
using MetaAnime.Application.Contracts;
using MetaAnime.Core;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Queries;

public sealed class GetSyncStatusQueryHandler(IPropertyBagRepository propertyBagRepository)
    : IRequestHandler<GetSyncStatusQuery, Result<SyncStatusResponse>>
{
    public async Task<Result<SyncStatusResponse>> Handle(GetSyncStatusQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var entry = await propertyBagRepository.GetByKeyAsync(SettingsKeys.LastSyncDate);

            if (entry == null || string.IsNullOrWhiteSpace(entry.Value))
                return Result.Success(new SyncStatusResponse(null));

            return DateTimeOffset.TryParse(entry.Value, out var dt)
                ? Result.Success(new SyncStatusResponse(dt))
                : Result.Success(new SyncStatusResponse(null));
        }
        catch (Exception ex)
        {
            return Result.Failure<SyncStatusResponse>(new(Constants.Errors.GetSyncStatus, ex.Message));
        }
    }
}
