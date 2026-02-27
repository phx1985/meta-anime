using Mediator;
using MetaAnime.Application.Contracts;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Queries;

public record GetAnimeStatisticsQuery() : IRequest<Result<AnimeStatisticsResponse>>;
