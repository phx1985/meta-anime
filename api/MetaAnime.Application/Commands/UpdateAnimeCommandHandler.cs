using MediatR;
using MetaAnime.Application.Interfaces;
using MetaAnime.Application.DTOs;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Commands;

public class UpdateAnimeCommandHandler(IAnimeMediatorService animeMediatorService)
    : IRequestHandler<UpdateAnimeCommand, Result<AnimeDto>>
{
    public async Task<Result<AnimeDto>> Handle(UpdateAnimeCommand request, CancellationToken cancellationToken)
    {
        return await animeMediatorService.UpdateAsync(request.Id, request.Request);
    }
}
