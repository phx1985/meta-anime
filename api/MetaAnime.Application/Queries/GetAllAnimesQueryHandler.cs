using MediatR;
using MetaAnime.Application.Interfaces;
using MetaAnime.Application.Contracts;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Queries;

public class GetAllAnimesQueryHandler(IAnimeMediatorService animeMediatorService)
    : IRequestHandler<GetAllAnimesQuery, Result<IEnumerable<AnimeDetailsResponse>>>
{
    public async Task<Result<IEnumerable<AnimeDetailsResponse>>> Handle(GetAllAnimesQuery request, CancellationToken cancellationToken)
    {
        return await animeMediatorService.GetAllAsync();
    }
}
