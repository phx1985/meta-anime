using MediatR;
using MetaAnime.Application.Interfaces;
using MetaAnime.Application.Contracts;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Commands;

public sealed class CreateAnimeCommandHandler(IAnimeMediatorService animeMediatorService)
    : IRequestHandler<CreateAnimeCommand, Result<AnimeResponse>>
{
    public async Task<Result<AnimeResponse>> Handle(CreateAnimeCommand request, CancellationToken cancellationToken)
    {
        return await animeMediatorService.CreateAsync(request.Request);
    }
}
