using MediatR;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Commands;

public class MatchAnimeCommandHandler(IAnimeMatchingService animeMatchingService)
    : IRequestHandler<MatchAnimeCommand, Result>
{
    public async Task<Result> Handle(MatchAnimeCommand request, CancellationToken cancellationToken)
    {
        return await animeMatchingService.MatchAsync(request.ForceRefreshKometaMapping);
    }
}
