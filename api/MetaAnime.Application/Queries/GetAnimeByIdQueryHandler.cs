using Mediator;
using MetaAnime.Application.Interfaces;
using MetaAnime.Application.Contracts;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Queries;

public sealed class GetAnimeByIdQueryHandler(IAnimeMediatorService animeMediatorService)
    : IRequestHandler<GetAnimeByIdQuery, Result<AnimeDetailsResponse?>>
{
    public async ValueTask<Result<AnimeDetailsResponse?>> Handle(GetAnimeByIdQuery request, CancellationToken cancellationToken)
    {
        return await animeMediatorService.GetByIdAsync(request.AniDbId);
    }
}
