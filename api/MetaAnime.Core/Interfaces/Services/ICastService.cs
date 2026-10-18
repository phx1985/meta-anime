using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Core.Interfaces.Services;

/// <summary>
/// Casts media to a remote playback device. Google Cast is the first provider; the abstraction is
/// deliberately provider-agnostic so DLNA / AirPlay / Miracast targets can be added later without
/// changing callers — each provider just reports devices with its own <see cref="CastDevice.Type"/>.
/// </summary>
public interface ICastService
{
    /// <summary>Discovers cast-capable devices on the local network.</summary>
    Task<Result<IList<CastDevice>>> GetDevicesAsync();

    /// <summary>
    /// Resolves the next episode to watch for <paramref name="showRatingKey"/> and casts it to the
    /// device at <paramref name="deviceHost"/>, resuming at <paramref name="offsetMs"/>.
    /// </summary>
    Task<Result<CastResult>> CastShowAsync(string deviceHost, string showRatingKey, long offsetMs = 0);
}

/// <summary>A cast-capable device. <c>Type</c> is "google-cast" today; later "dlna" / "airplay".</summary>
public sealed record CastDevice(
    string Id,
    string Name,
    string Type,
    string Host,
    int Port,
    string? Model);

/// <summary>Result of successfully starting a cast.</summary>
public sealed record CastResult(
    string DeviceName,
    string Title,
    string SubTitle,
    string StreamUrl);
