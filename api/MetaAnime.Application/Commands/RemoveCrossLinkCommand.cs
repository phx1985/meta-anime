using Mediator;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Commands;

public record RemoveCrossLinkCommand(string SourceAniDbId, string MalId) : IRequest<Result>;
