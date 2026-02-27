using Mediator;
using MetaAnime.Application.Interfaces;
using MetaAnime.Application.Contracts;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Queries;

public sealed class GetAllAnimesQueryHandler(IAnimeMediatorService animeMediatorService)
    : IRequestHandler<GetAllAnimesQuery, Result<PagedResponse<IEnumerable<AnimeDetailsResponse>>>>
{
    public async ValueTask<Result<PagedResponse<IEnumerable<AnimeDetailsResponse>>>> Handle(GetAllAnimesQuery request, CancellationToken cancellationToken)
    {
        return await animeMediatorService.GetAllAsync(request.Page, request.PageSize, request.Filters, request.Enriched);
    }
}
