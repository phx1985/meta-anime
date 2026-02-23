using MediatR;
using MetaAnime.Application.Contracts;
using MetaAnime.Application.Interfaces;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Queries;

public class GetSeasonalAnimesQueryHandler(IAnimeMediatorService animeMediatorService)
    : IRequestHandler<GetSeasonalAnimesQuery, Result<PagedResponse<IEnumerable<SeasonalAnimeResponse>>>>
{
    public async Task<Result<PagedResponse<IEnumerable<SeasonalAnimeResponse>>>> Handle(GetSeasonalAnimesQuery request, CancellationToken cancellationToken)
    {
        return await animeMediatorService.GetSeasonalAnimesAsync(request.Year, request.Season, request.Page, request.PageSize);
    }
}
