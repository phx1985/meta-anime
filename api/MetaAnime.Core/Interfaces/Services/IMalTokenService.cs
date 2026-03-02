using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Core.Interfaces.Services;

public interface IMalTokenService
{
    /// <summary>
    /// Returns the current MAL access token, reading exclusively from the PropertyBag.
    /// </summary>
    Task<string?> GetAccessTokenAsync();

    /// <summary>
    /// Refreshes the MAL access token using the stored or configured refresh token and persists the new tokens.
    /// </summary>
    Task<Result> RefreshTokenAsync();

    /// <summary>
    /// Returns true when a MAL access token is present in the property bag.
    /// </summary>
    Task<bool> IsConnectedAsync();
}
