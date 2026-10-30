using Mediator;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Commands;

public record DisableAuthCommand(string CurrentPassword) : IRequest<Result>;
