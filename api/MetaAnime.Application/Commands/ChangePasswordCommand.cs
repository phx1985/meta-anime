using Mediator;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Commands;

public record ChangePasswordCommand(string CurrentPassword, string NewPassword) : IRequest<Result>;
