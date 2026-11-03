using Mediator;
using MetaAnime.Application.Contracts;
using MetaAnime.Application.Interfaces;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Queries;

public sealed class GetSeasonalSuggestionsQueryHandler(IAnimeMediatorService animeMediatorService)
    : IRequestHandler<GetSeasonalSuggestionsQuery, Result<IEnumerable<SeasonalSuggestionResponse>>>
{
    public async ValueTask<Result<IEnumerable<SeasonalSuggestionResponse>>> Handle(
        GetSeasonalSuggestionsQuery request,
        CancellationToken cancellationToken)
    {
        return await animeMediatorService.GetSeasonalSuggestionsAsync(
            request.Year, request.Season, request.Limit, request.Scope,
            request.FromYear, request.FromSeason, request.ToYear, request.ToSeason,
            request.ExcludeChinese, request.ExcludeGenres);
    }
}
