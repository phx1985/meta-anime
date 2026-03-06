using Mediator;
using MetaAnime.Application.Interfaces;
using MetaAnime.Application.Contracts;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Queries;

public sealed class GetAnimeByMalIdQueryHandler(IAnimeMediatorService animeMediatorService)
    : IRequestHandler<GetAnimeByMalIdQuery, Result<AnimeDetailsResponse?>>
{
    public async ValueTask<Result<AnimeDetailsResponse?>> Handle(GetAnimeByMalIdQuery request, CancellationToken cancellationToken)
    {
        return await animeMediatorService.GetByMalIdAsync(request.MalId);
    }
}
