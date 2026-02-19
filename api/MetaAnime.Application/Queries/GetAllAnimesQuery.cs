using MediatR;
using MetaAnime.Application.Contracts;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Queries;

public record GetAllAnimesQuery(
    int Page = 1, 
    int PageSize = 100,
    AnimeFilters? Filters = null
) : IRequest<Result<PagedResponse<IEnumerable<AnimeDetailsResponse>>>>;
