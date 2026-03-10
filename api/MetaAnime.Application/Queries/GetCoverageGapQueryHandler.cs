using Mediator;
using MetaAnime.Application.Contracts;
using MetaAnime.Application.Interfaces;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Queries;

public sealed class GetCoverageGapQueryHandler(IAnimeMediatorService animeMediatorService)
    : IRequestHandler<GetCoverageGapQuery, Result<IEnumerable<CoverageGapItemResponse>>>
{
    public async ValueTask<Result<IEnumerable<CoverageGapItemResponse>>> Handle(
        GetCoverageGapQuery request,
        CancellationToken cancellationToken)
    {
        return await animeMediatorService.GetCoverageGapAsync(request.GapType);
    }
}
