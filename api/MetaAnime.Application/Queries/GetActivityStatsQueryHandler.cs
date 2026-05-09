using Mediator;
using MetaAnime.Application.Contracts;
using MetaAnime.Application.Interfaces;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Queries;

public sealed class GetActivityStatsQueryHandler(IAnimeMediatorService animeMediatorService)
    : IRequestHandler<GetActivityStatsQuery, Result<ActivityStatsResponse>>
{
    public async ValueTask<Result<ActivityStatsResponse>> Handle(GetActivityStatsQuery request, CancellationToken cancellationToken)
    {
        return await animeMediatorService.GetActivityStatsAsync(request.TzOffsetMinutes);
    }
}
