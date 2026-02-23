using MediatR;
using MetaAnime.Application.Contracts;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Queries;

public record GetSeasonalAnimesQuery(int Year, string Season, int Page, int PageSize)
    : IRequest<Result<PagedResponse<IEnumerable<SeasonalAnimeResponse>>>>;
