using Mediator;
using MetaAnime.Application.Contracts;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Queries;

public sealed record GetActivityStatsQuery(int TzOffsetMinutes = 0) : IRequest<Result<ActivityStatsResponse>>;
