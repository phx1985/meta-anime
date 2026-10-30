namespace MetaAnime.Application.Contracts;

/// <summary>Response for the authenticated runtime-key endpoint (matches the /assets/config.json shape).</summary>
public sealed record RuntimeKeyResponse(string ApiKey);
