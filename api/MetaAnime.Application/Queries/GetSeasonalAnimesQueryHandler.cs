using MediatR;
using MetaAnime.Application.Contracts;
using MetaAnime.Application.Interfaces;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Queries;

public class GetSeasonalAnimesQueryHandler(IAnimeMediatorService animeMediatorService)
    : IRequestHandler<GetSeasonalAnimesQuery, Result<IEnumerable<SeasonalAnimeResponse>>>
{
    public async Task<Result<IEnumerable<SeasonalAnimeResponse>>> Handle(GetSeasonalAnimesQuery request, CancellationToken cancellationToken)
    {
        return await animeMediatorService.GetSeasonalAnimesAsync(request.Year, request.Season);
    }
}
