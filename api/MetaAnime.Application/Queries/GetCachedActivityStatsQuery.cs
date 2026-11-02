using Mediator;
using MetaAnime.Application.Contracts;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Queries;

public sealed record GetCachedActivityStatsQuery(int TzOffsetMinutes = 0) : IRequest<Result<ActivityStatsResponse?>>;
