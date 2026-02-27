using Mediator;
using MetaAnime.Application.Contracts;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Queries;

public sealed class GetAllPropertyBagQueryHandler(IPropertyBagRepository propertyBagRepository)
    : IRequestHandler<GetAllPropertyBagQuery, Result<IEnumerable<PropertyBagResponse>>>
{
    public async ValueTask<Result<IEnumerable<PropertyBagResponse>>> Handle(GetAllPropertyBagQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var entries = await propertyBagRepository.GetAllAsync();
            var response = entries.Select(e => new PropertyBagResponse(e.Key, e.Value, e.CreationDate, e.UpdateDate));
            return Result.Success(response);
        }
        catch (Exception ex)
        {
            return Result.Failure<IEnumerable<PropertyBagResponse>>(new(Constants.Errors.GetAllPropertyBag, ex.Message));
        }
    }
}
