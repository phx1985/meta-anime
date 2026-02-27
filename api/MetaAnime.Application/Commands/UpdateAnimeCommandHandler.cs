using MediatR;
using MetaAnime.Application.Interfaces;
using MetaAnime.Application.Contracts;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Commands;

public sealed class UpdateAnimeCommandHandler(IAnimeMediatorService animeMediatorService)
    : IRequestHandler<UpdateAnimeCommand, Result<AnimeResponse>>
{
    public async Task<Result<AnimeResponse>> Handle(UpdateAnimeCommand request, CancellationToken cancellationToken)
    {
        return await animeMediatorService.UpdateAsync(request.Id, request.Request);
    }
}
