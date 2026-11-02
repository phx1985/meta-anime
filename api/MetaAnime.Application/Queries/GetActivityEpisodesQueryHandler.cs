using Mediator;
using MetaAnime.Application.Contracts;
using MetaAnime.Application.Interfaces;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Queries;

public sealed class GetActivityEpisodesQueryHandler(IAnimeMediatorService animeMediatorService)
    : IRequestHandler<GetActivityEpisodesQuery, Result<ActivityEpisodesResponse>>
{
    public async ValueTask<Result<ActivityEpisodesResponse>> Handle(
        GetActivityEpisodesQuery request,
        CancellationToken cancellationToken)
    {
        return await animeMediatorService.GetActivityEpisodesAsync(request.Period, request.TzOffsetMinutes, request.Limit);
    }
}
