using MediatR;
using MetaAnime.Application.DTOs;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Queries;

public record GetAllAnimesQuery : IRequest<Result<IEnumerable<GetAnimeDto>>>;
