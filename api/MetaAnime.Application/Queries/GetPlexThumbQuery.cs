using Mediator;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Queries;

public sealed record GetPlexThumbQuery(string RatingKey) : IRequest<Result<byte[]>>;
