using Mediator;
using MetaAnime.Application.Contracts;
using MetaAnime.Core;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Queries;

public sealed class GetAuthStatusQueryHandler(IPropertyBagRepository propertyBagRepository)
    : IRequestHandler<GetAuthStatusQuery, Result<AuthStatusResponse>>
{
    public async ValueTask<Result<AuthStatusResponse>> Handle(GetAuthStatusQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var entry = await propertyBagRepository.GetByKeyAsync(SettingsKeys.AdminPasswordHash);
            return Result.Success(new AuthStatusResponse(!string.IsNullOrWhiteSpace(entry?.Value)));
        }
        catch (Exception ex)
        {
            return Result.Failure<AuthStatusResponse>(new(Constants.Errors.GetAuthStatus, ex.Message));
        }
    }
}
