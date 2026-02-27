using Mediator;
using MetaAnime.Application.Contracts;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Commands;

public record UpdateAnimeCommand(Guid Id, UpdateAnimeRequest Request) : IRequest<Result<AnimeResponse>>;
