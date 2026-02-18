using MediatR;
using MetaAnime.Application.Interfaces;
using MetaAnime.Application.DTOs;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Queries;

public class GetAllAnimesQueryHandler(IAnimeMediatorService animeMediatorService)
    : IRequestHandler<GetAllAnimesQuery, Result<IEnumerable<GetAnimeDto>>>
{
    public async Task<Result<IEnumerable<GetAnimeDto>>> Handle(GetAllAnimesQuery request, CancellationToken cancellationToken)
    {
        return await animeMediatorService.GetAllAsync();
    }
}
