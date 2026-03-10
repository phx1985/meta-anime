using Mediator;
using MetaAnime.Application.Contracts;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Queries;

/// <param name="GapType">
/// One of: <c>sonarrNotInMal</c>, <c>malNotInPlex</c>, <c>plexNotInMal</c>, <c>plexNotInSonarr</c>.
/// </param>
public sealed record GetCoverageGapQuery(string GapType)
    : IRequest<Result<IEnumerable<CoverageGapItemResponse>>>;
