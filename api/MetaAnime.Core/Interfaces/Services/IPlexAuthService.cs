using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Core.Interfaces.Services;

public interface IPlexAuthService
{
    /// <summary>
    /// Creates a Plex pin via plex.tv and returns the pin data needed to build the auth URL.
    /// </summary>
    Task<Result<PlexPinData>> InitiatePinAsync();

    /// <summary>
    /// Polls plex.tv for the given pin. If the user has authorized, persists the auth token.
    /// Returns true when the token has been stored.
    /// </summary>
    Task<Result<bool>> PollPinAsync(int pinId);

    /// <summary>
    /// Removes the stored Plex auth token from the property bag.
    /// </summary>
    Task<Result> DisconnectAsync();

    /// <summary>
    /// Returns true when a Plex auth token is present in the property bag.
    /// </summary>
    Task<bool> IsConnectedAsync();
}

/// <summary>Data returned when a Plex OAuth pin is created.</summary>
public sealed record PlexPinData(int Id, string Code, string AuthUrl);
