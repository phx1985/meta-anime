using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Core.Interfaces.Services;

public interface IMalOAuthService
{
    /// <summary>
    /// Generates the MAL OAuth authorization URL (PKCE flow).
    /// Persists the code_verifier and state for later verification.
    /// </summary>
    Task<Result<string>> GetAuthorizationUrlAsync(string redirectUri);

    /// <summary>
    /// Exchanges the authorization code (and stored code_verifier) for access and refresh tokens.
    /// Persists the tokens in the property bag.
    /// </summary>
    Task<Result> ExchangeCodeAsync(string code, string state, string redirectUri);

    /// <summary>
    /// Removes all stored MAL tokens from the property bag.
    /// </summary>
    Task<Result> DisconnectAsync();

    /// <summary>
    /// Returns true when a MAL access token is present in the property bag.
    /// </summary>
    Task<bool> IsConnectedAsync();
}
