using Mediator;
using MetaAnime.Application.Contracts;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Commands;

public record LoginCommand(string Password) : IRequest<Result<LoginResponse>>;
