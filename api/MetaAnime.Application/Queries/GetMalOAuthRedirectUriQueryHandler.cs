using Mediator;
using MetaAnime.Core;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Queries;

public sealed class GetMalOAuthRedirectUriQueryHandler(IPropertyBagRepository propertyBagRepository)
    : IRequestHandler<GetMalOAuthRedirectUriQuery, Result<string>>
{
    public async ValueTask<Result<string>> Handle(GetMalOAuthRedirectUriQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var entry = await propertyBagRepository.GetByKeyAsync(SettingsKeys.MalOAuthRedirectUri);
            return Result.Success(entry?.Value ?? string.Empty);
        }
        catch (Exception ex)
        {
            return Result.Failure<string>(new(Constants.Errors.GetMalOAuthRedirectUri, ex.Message));
        }
    }
}
