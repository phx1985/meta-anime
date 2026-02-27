using Mediator;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Commands;

public record DeletePropertyBagCommand(string Key) : IRequest<Result>;
