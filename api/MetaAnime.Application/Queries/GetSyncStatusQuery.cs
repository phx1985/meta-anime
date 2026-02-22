using MediatR;
using MetaAnime.Application.Contracts;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Queries;

public record GetSyncStatusQuery : IRequest<Result<SyncStatusResponse>>;
