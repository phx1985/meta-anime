using Mediator;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Commands;

public sealed class MatchAnimeCommandHandler(
    IAnimeMatchingService animeMatchingService,
    ISyncProgressNotifier progressNotifier)
    : IRequestHandler<MatchAnimeCommand, Result>
{
    public async ValueTask<Result> Handle(MatchAnimeCommand request, CancellationToken cancellationToken)
    {
        return await animeMatchingService.MatchAsync(request.ForceRefreshKometaMapping, progressNotifier);
    }
}
