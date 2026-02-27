using Mediator;
using MetaAnime.Application.Contracts;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Queries;

public record GetAllPropertyBagQuery : IRequest<Result<IEnumerable<PropertyBagResponse>>>;
