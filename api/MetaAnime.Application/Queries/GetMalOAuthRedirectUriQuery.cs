using Mediator;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Queries;

/// <summary>Returns the MAL OAuth redirect URI stored by the enrollment flow (empty when absent).</summary>
public record GetMalOAuthRedirectUriQuery : IRequest<Result<string>>;
