using MediatR;
using MetaAnime.Application.DTOs;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Commands;

public record UpdateAnimeCommand(Guid Id, UpdateAnimeRequest Request) : IRequest<Result<AnimeDto>>;
