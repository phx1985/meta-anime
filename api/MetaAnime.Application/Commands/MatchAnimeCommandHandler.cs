using MediatR;
using MetaAnime.Core;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Commands;

public class MatchAnimeCommandHandler(
    IAnimeMatchingService animeMatchingService,
    ISyncProgressNotifier progressNotifier,
    IPropertyBagRepository propertyBagRepository)
    : IRequestHandler<MatchAnimeCommand, Result>
{
    public async Task<Result> Handle(MatchAnimeCommand request, CancellationToken cancellationToken)
    {
        var result = await animeMatchingService.MatchAsync(request.ForceRefreshKometaMapping, progressNotifier);

        if (result.IsSuccess)
        {
            await propertyBagRepository.UpsertAsync(SettingsKeys.LastSyncDate, DateTime.UtcNow.ToString("O"));
        }

        return result;
    }
}
