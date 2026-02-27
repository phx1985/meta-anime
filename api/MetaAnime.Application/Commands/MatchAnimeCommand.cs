using Mediator;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Commands;

public record MatchAnimeCommand(bool ForceRefreshKometaMapping = false) : IRequest<Result>;
