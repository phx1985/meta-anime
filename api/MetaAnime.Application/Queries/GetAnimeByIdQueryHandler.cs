using MediatR;
using MetaAnime.Application.Interfaces;
using MetaAnime.Application.DTOs;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Queries;

public class GetAnimeByIdQueryHandler(IAnimeMediatorService animeMediatorService)
    : IRequestHandler<GetAnimeByIdQuery, Result<GetAnimeDto?>>
{
    public async Task<Result<GetAnimeDto?>> Handle(GetAnimeByIdQuery request, CancellationToken cancellationToken)
    {
        return await animeMediatorService.GetByIdAsync(request.AniDbId);
    }
}
