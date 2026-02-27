using MediatR;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Commands;

public record DeleteAnimeCommand(Guid Id) : IRequest<Result>;
