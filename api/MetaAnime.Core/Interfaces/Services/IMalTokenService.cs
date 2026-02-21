using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Core.Interfaces.Services;

public interface IMalTokenService
{
    /// <summary>
    /// Returns the current MAL access token, reading from PropertyBag first, then falling back to settings.
    /// </summary>
    Task<string?> GetAccessTokenAsync();

    /// <summary>
    /// Refreshes the MAL access token using the stored or configured refresh token and persists the new tokens.
    /// </summary>
    Task<Result> RefreshTokenAsync();
}
