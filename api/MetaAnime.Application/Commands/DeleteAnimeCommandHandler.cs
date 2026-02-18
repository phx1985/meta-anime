using MediatR;
using MetaAnime.Application.Interfaces;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Commands;

public class DeleteAnimeCommandHandler(IAnimeMediatorService animeMediatorService)
    : IRequestHandler<DeleteAnimeCommand, Result>
{
    public async Task<Result> Handle(DeleteAnimeCommand request, CancellationToken cancellationToken)
    {
        return await animeMediatorService.DeleteAsync(request.Id);
    }
}
