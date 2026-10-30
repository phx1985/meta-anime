using Mediator;
using MetaAnime.Application.Contracts;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Queries;

public sealed class GetPropertyBagByKeyQueryHandler(IPropertyBagRepository propertyBagRepository)
    : IRequestHandler<GetPropertyBagByKeyQuery, Result<PropertyBagResponse?>>
{
    public async ValueTask<Result<PropertyBagResponse?>> Handle(GetPropertyBagByKeyQuery request, CancellationToken cancellationToken)
    {
        try
        {
            // Internal System:* keys are never exposed through the generic
            // property-bag API — report them exactly like missing keys.
            if (PropertyBagKeyPolicy.IsSystemManaged(request.Key))
            {
                return Result.Success<PropertyBagResponse?>(null);
            }

            var entry = await propertyBagRepository.GetByKeyAsync(request.Key);
            if (entry is null)
            {
                return Result.Success<PropertyBagResponse?>(null);
            }

            return Result.Success<PropertyBagResponse?>(new PropertyBagResponse(entry.Key, entry.Value, entry.CreationDate, entry.UpdateDate));
        }
        catch (Exception ex)
        {
            return Result.Failure<PropertyBagResponse?>(new(Constants.Errors.GetPropertyBagByKey, ex.Message));
        }
    }
}
