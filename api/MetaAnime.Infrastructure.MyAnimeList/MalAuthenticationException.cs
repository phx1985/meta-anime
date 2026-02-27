namespace MetaAnime.Infrastructure.MyAnimeList;

/// <summary>
/// Thrown when the MyAnimeList API returns 401 Unauthorized and a token refresh attempt fails.
/// </summary>
public sealed class MalAuthenticationException(string message) : Exception(message);
