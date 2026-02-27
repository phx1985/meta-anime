using Mediator;
using MetaAnime.Application.Interfaces;
using MetaAnime.Application.Contracts;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Queries;

public sealed class GetAnimeStatisticsQueryHandler(IAnimeMediatorService animeMediatorService)
    : IRequestHandler<GetAnimeStatisticsQuery, Result<AnimeStatisticsResponse>>
{
    public async ValueTask<Result<AnimeStatisticsResponse>> Handle(GetAnimeStatisticsQuery request, CancellationToken cancellationToken)
    {
        return await animeMediatorService.GetStatisticsAsync();
    }
}
