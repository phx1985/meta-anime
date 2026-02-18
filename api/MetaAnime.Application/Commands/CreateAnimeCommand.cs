using MediatR;
using MetaAnime.Application.Contracts;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Commands;

public record CreateAnimeCommand(CreateAnimeRequest Request) : IRequest<Result<AnimeResponse>>;
