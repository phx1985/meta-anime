using MediatR;
using MetaAnime.Application.Contracts;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Queries;

public class GetPropertyBagByKeyQueryHandler(IPropertyBagRepository propertyBagRepository)
    : IRequestHandler<GetPropertyBagByKeyQuery, Result<PropertyBagResponse?>>
{
    public async Task<Result<PropertyBagResponse?>> Handle(GetPropertyBagByKeyQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var entry = await propertyBagRepository.GetByKeyAsync(request.Key);
            if (entry is null)
            {
                return Result.Success<PropertyBagResponse?>(null);
            }

            return Result.Success<PropertyBagResponse?>(new PropertyBagResponse(entry.Key, entry.Value, entry.CreationDate, entry.UpdateDate));
        }
        catch (Exception ex)
        {
            return Result.Failure<PropertyBagResponse?>(new("Error.GetPropertyBagByKey", ex.Message));
        }
    }
}
