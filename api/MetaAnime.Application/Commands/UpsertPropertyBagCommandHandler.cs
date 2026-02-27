using MediatR;
using MetaAnime.Application.Contracts;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Commands;

public sealed class UpsertPropertyBagCommandHandler(IPropertyBagRepository propertyBagRepository)
    : IRequestHandler<UpsertPropertyBagCommand, Result<PropertyBagResponse>>
{
    public async Task<Result<PropertyBagResponse>> Handle(UpsertPropertyBagCommand request, CancellationToken cancellationToken)
    {
        try
        {
            await propertyBagRepository.UpsertAsync(request.Key, request.Request.Value);
            var entry = await propertyBagRepository.GetByKeyAsync(request.Key);
            return Result.Success(new PropertyBagResponse(entry!.Key, entry.Value, entry.CreationDate, entry.UpdateDate));
        }
        catch (Exception ex)
        {
            return Result.Failure<PropertyBagResponse>(new(Constants.Errors.UpsertPropertyBag, ex.Message));
        }
    }
}
