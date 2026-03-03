namespace MetaAnime.Application.Contracts;

/// <summary>Response for the public GET /auth/status endpoint.</summary>
public sealed record AuthStatusResponse(bool RequiresAuth);
