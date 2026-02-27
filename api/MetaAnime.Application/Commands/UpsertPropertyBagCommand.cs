using Mediator;
using MetaAnime.Application.Contracts;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Commands;

public record UpsertPropertyBagCommand(string Key, UpsertPropertyBagRequest Request) : IRequest<Result<PropertyBagResponse>>;
