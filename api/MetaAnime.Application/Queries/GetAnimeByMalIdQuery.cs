using Mediator;
using MetaAnime.Application.Contracts;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Queries;

public record GetAnimeByMalIdQuery(string MalId) : IRequest<Result<AnimeDetailsResponse?>>;
