using Mediator;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Commands;

/// <summary>Removes a user-confirmed AniDbId cross-link and its bridge mapping.</summary>
public sealed class RemoveCrossLinkCommandHandler(
    ICrossLinkService crossLinkService,
    IAnimeMappingRepository animeMappingRepository)
    : IRequestHandler<RemoveCrossLinkCommand, Result>
{
    public async ValueTask<Result> Handle(RemoveCrossLinkCommand request, CancellationToken cancellationToken)
    {
        try
        {
            await crossLinkService.RemoveAsync(request.SourceAniDbId, request.MalId);
            await animeMappingRepository.DeleteByAniDbIdAsync(request.SourceAniDbId, request.MalId);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(new(Constants.Errors.RemoveCrossLink, ex.Message));
        }
    }
}
