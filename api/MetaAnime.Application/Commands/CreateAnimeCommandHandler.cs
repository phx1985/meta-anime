using MediatR;
using MetaAnime.Application.Interfaces;
using MetaAnime.Application.DTOs;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Commands;

public class CreateAnimeCommandHandler(IAnimeMediatorService animeMediatorService)
    : IRequestHandler<CreateAnimeCommand, Result<AnimeDto>>
{
    public async Task<Result<AnimeDto>> Handle(CreateAnimeCommand request, CancellationToken cancellationToken)
    {
        return await animeMediatorService.CreateAsync(request.Request);
    }
}
