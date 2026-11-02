using Mediator;
using MetaAnime.Application.Contracts;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Queries;

/// <param name="Period">Which activity tab the caller is looking at.</param>
/// <param name="TzOffsetMinutes">JavaScript <c>Date.getTimezoneOffset()</c> convention (UTC − local).</param>
/// <param name="Limit">Maximum number of episode rows to return (newest first).</param>
public sealed record GetActivityEpisodesQuery(
    ActivityStatsPeriod Period,
    int TzOffsetMinutes = 0,
    int Limit = 250) : IRequest<Result<ActivityEpisodesResponse>>;
