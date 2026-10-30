using Mediator;
using MetaAnime.Application.Contracts;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Queries;

public record GetCrossLinksQuery : IRequest<Result<IReadOnlyList<CrossLinkResponse>>>;
