using Mediator;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Interfaces.Shared.Results;
using MetaAnime.Infrastructure.Plex.Models;

namespace MetaAnime.Application.Queries;

public sealed class GetPlexThumbQueryHandler(IPlexAnimeSourceService<PlexMedia> plexAnimeSource)
    : IRequestHandler<GetPlexThumbQuery, Result<byte[]>>
{
    public async ValueTask<Result<byte[]>> Handle(GetPlexThumbQuery request, CancellationToken cancellationToken)
        => await plexAnimeSource.GetThumbBytesAsync(request.RatingKey);
}
