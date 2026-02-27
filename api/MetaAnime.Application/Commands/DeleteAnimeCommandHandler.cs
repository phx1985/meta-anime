using Mediator;
using MetaAnime.Application.Interfaces;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Commands;

public sealed class DeleteAnimeCommandHandler(IAnimeMediatorService animeMediatorService)
    : IRequestHandler<DeleteAnimeCommand, Result>
{
    public async ValueTask<Result> Handle(DeleteAnimeCommand request, CancellationToken cancellationToken)
    {
        return await animeMediatorService.DeleteAsync(request.Id);
    }
}
