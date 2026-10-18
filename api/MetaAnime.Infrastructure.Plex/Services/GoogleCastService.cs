using System.Text.Json;
using MetaAnime.Core;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Interfaces.Shared.Results;
using Sharpcaster;
using Sharpcaster.Models;
using Sharpcaster.Models.Media;

namespace MetaAnime.Infrastructure.Plex.Services;

/// <summary>
/// Native Google Cast (Cast V2) playback of Plex content, without the Plex client app.
///
/// Discovers Cast devices over mDNS (SharpCaster), then for a show resolves the next episode to
/// watch (On-Deck / next unwatched) from the Plex server, builds a direct stream URL for its media
/// part, and loads it into the Chromecast's built-in Default Media Receiver (CC1AD845) so the device
/// streams straight from Plex. Watch-state sync back to Plex is handled separately (timeline).
///
/// Docker note: mDNS discovery requires host networking. On a bridge network, discovery may be
/// empty — the device can still be reached by passing its static LAN host.
/// </summary>
public sealed class GoogleCastService(IPropertyBagRepository propertyBagRepository) : ICastService
{
    private const string DefaultMediaReceiverAppId = "CC1AD845";

    // ── Discovery ──────────────────────────────────────────────────────────────────────────────

    public async Task<Result<IList<CastDevice>>> GetDevicesAsync()
    {
        try
        {
            using var locator = new ChromecastLocator();
            var receivers = await locator.FindReceiversAsync(TimeSpan.FromSeconds(4));

            var devices = (receivers ?? Enumerable.Empty<ChromecastReceiver>())
                .Where(r => !string.IsNullOrWhiteSpace(r.DeviceUri?.Host) && IsVideoCastDevice(r.Model))
                .Select(r => new CastDevice(
                    r.DeviceUri!.Host,
                    string.IsNullOrWhiteSpace(r.Name) ? r.DeviceUri.Host : r.Name,
                    "google-cast",
                    r.DeviceUri!.Host,
                    r.Port,
                    r.Model))
                .ToList();

            return Result.Success<IList<CastDevice>>(devices);
        }
        catch (Exception ex)
        {
            return Result.Failure<IList<CastDevice>>(new Error(
                Constants.Errors.CastDiscoveryFailed, $"Cast discovery failed: {ex.Message}"));
        }
    }

    // ── Cast ───────────────────────────────────────────────────────────────────────────────────

    public async Task<Result<CastResult>> CastShowAsync(string deviceHost, string showRatingKey, long offsetMs = 0)
    {
        if (string.IsNullOrWhiteSpace(deviceHost))
            return Result.Failure<CastResult>(new Error(Constants.Errors.CastFailed, "A target device is required."));
        if (string.IsNullOrWhiteSpace(showRatingKey))
            return Result.Failure<CastResult>(new Error(Constants.Errors.CastFailed, "A show rating key is required."));

        var (ok, host, token) = await GetPlexConnectionAsync();
        if (!ok)
            return Result.Failure<CastResult>(new Error(Constants.Errors.NotConfigured, "Plex is not configured."));

        // 1. Resolve the next episode + build its stream URL.
        var ep = await ResolveNextEpisodeAsync(host, token, showRatingKey);
        if (ep is null)
            return Result.Failure<CastResult>(new Error(
                Constants.Errors.CastFailed, "No playable episode found for that show."));

        var streamUrl = $"{host}{ep.PartKey}?X-Plex-Token={Uri.EscapeDataString(token)}";
        var posterUrl = string.IsNullOrWhiteSpace(ep.Thumb)
            ? null
            : $"{host}{ep.Thumb}?X-Plex-Token={Uri.EscapeDataString(token)}";

        // 2. Find the target Cast device.
        var found = await GetDevicesAsync();
        if (found.IsFailure)
            return Result.Failure<CastResult>(found.Error);
        var device = found.Value.FirstOrDefault(d =>
            string.Equals(d.Host, deviceHost, StringComparison.OrdinalIgnoreCase));
        if (device is null)
            return Result.Failure<CastResult>(new Error(
                Constants.Errors.CastFailed, $"Cast device '{deviceHost}' was not found on the network."));

        // 3. Connect to it and load the media into the Default Media Receiver.
        try
        {
            using var locator = new ChromecastLocator();
            var receivers = await locator.FindReceiversAsync(TimeSpan.FromSeconds(4));
            var receiver = (receivers ?? Enumerable.Empty<ChromecastReceiver>())
                .FirstOrDefault(r => string.Equals(r.DeviceUri?.Host, deviceHost, StringComparison.OrdinalIgnoreCase));
            if (receiver is null)
                return Result.Failure<CastResult>(new Error(
                    Constants.Errors.CastFailed, $"Cast device '{deviceHost}' disappeared during discovery."));

            var client = new ChromecastClient();
            await client.ConnectChromecast(receiver);
            await client.LaunchApplicationAsync(DefaultMediaReceiverAppId, false);

            var media = new Media
            {
                ContentUrl = streamUrl,
                ContentType = ContentTypeFor(ep.PartKey),
                StreamType = StreamType.Buffered,
                Metadata = new MediaMetadata
                {
                    Title = ep.Title,
                    SubTitle = ep.SubTitle,
                    Images = posterUrl is null ? Array.Empty<Image>() : new[] { new Image { Url = posterUrl } },
                },
            };

            await client.MediaChannel.LoadAsync(media, true, null);

            // Resume: prefer an explicit offset, else the episode's saved position (Plex ms -> Cast s).
            var startMs = offsetMs > 0 ? offsetMs : ep.ViewOffsetMs;
            if (startMs > 0)
                await client.MediaChannel.SeekAsync(startMs / 1000.0);

            return Result.Success(new CastResult(device.Name, ep.Title, ep.SubTitle, streamUrl));
        }
        catch (Exception ex)
        {
            return Result.Failure<CastResult>(new Error(
                Constants.Errors.CastFailed, $"Failed to cast to '{deviceHost}': {ex.Message}"));
        }
    }

    // ── Plex episode resolution ────────────────────────────────────────────────────────────────

    private sealed record EpisodeInfo(
        string RatingKey, string Title, string SubTitle, string PartKey, string? Thumb,
        long ViewOffsetMs, long DurationMs);

    /// <summary>Resolves the On-Deck / next-unwatched episode for a show and its first media part.</summary>
    private async Task<EpisodeInfo?> ResolveNextEpisodeAsync(string host, string token, string showRatingKey)
    {
        // Preferred: On-Deck (in-progress or next-up) episode.
        var onDeck = await FetchEpisodeAsync(
            $"{host}/library/metadata/{showRatingKey}?includeOnDeck=1", token, fromOnDeck: true);
        if (onDeck is not null)
            return onDeck;

        // Fallback: first episode that is unwatched (or partially watched).
        return await FetchEpisodeAsync(
            $"{host}/library/metadata/{showRatingKey}/allLeaves", token, fromOnDeck: false);
    }

    private async Task<EpisodeInfo?> FetchEpisodeAsync(string url, string token, bool fromOnDeck)
    {
        try
        {
            using var httpClient = CreatePlexClient(token);
            using var response = await httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                return null;
            await using var stream = await response.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);

            if (!TryGetCI(doc.RootElement, "MediaContainer", out var container))
                return null;

            JsonElement? ep = null;
            if (fromOnDeck)
            {
                // Show -> Metadata[0].OnDeck.Metadata[0]
                var shows = GetArray(container, "Metadata");
                var show = shows?.EnumerateArray().FirstOrDefault();
                if (show is JsonElement s && TryGetCI(s, "OnDeck", out var onDeck)
                    && TryGetCI(onDeck, "Metadata", out var onDeckMeta)
                    && onDeckMeta.ValueKind == JsonValueKind.Array && onDeckMeta.GetArrayLength() > 0)
                {
                    ep = onDeckMeta[0];
                }
            }
            else
            {
                // allLeaves -> first unwatched / in-progress episode.
                var leaves = GetArray(container, "Metadata");
                if (leaves is not null)
                {
                    foreach (var e in leaves.Value.EnumerateArray())
                    {
                        var viewCount = GetLongCI(e, "viewCount");
                        var viewOffset = GetLongCI(e, "viewOffset");
                        if (viewCount == 0 || viewOffset > 0)
                        {
                            ep = e;
                            break;
                        }
                    }
                }
            }

            if (ep is null)
                return null;

            var e0 = ep.Value;
            var partKey = FirstPartKey(e0);
            if (string.IsNullOrWhiteSpace(partKey))
                return null;

            var grandparent = GetStringCI(e0, "grandparentTitle") ?? string.Empty;
            var season = GetLongCI(e0, "parentIndex");
            var index = GetLongCI(e0, "index");
            var epTitle = GetStringCI(e0, "title") ?? "Episode";
            var subTitle = season > 0 && index > 0 ? $"S{season:00}E{index:00}" : epTitle;

            return new EpisodeInfo(
                GetStringCI(e0, "ratingKey") ?? string.Empty,
                string.IsNullOrWhiteSpace(grandparent) ? epTitle : grandparent,
                subTitle,
                partKey,
                GetStringCI(e0, "thumb"),
                GetLongCI(e0, "viewOffset"),
                GetLongCI(e0, "duration"));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// True for a Cast device that can display video (has a screen / video output). Audio-only
    /// Cast devices (Chromecast Audio, Google/Nest speakers, multi-room cast groups) cannot play
    /// video and are filtered out. Classified by the mDNS model string — the same approach
    /// pychromecast uses to derive a device's cast type.
    /// </summary>
    private static bool IsVideoCastDevice(string? model)
    {
        var m = model?.Trim().ToLowerInvariant() ?? string.Empty;
        if (m.Length == 0)
            return true; // Unknown model — keep it rather than hide a real video device.

        // "Chromecast Audio" is audio-only despite the Chromecast name.
        if (m.Contains("chromecast") && m.Contains("audio"))
            return false;

        // Devices that clearly render video (screens / video output).
        if (m.Contains("chromecast") || m.Contains("tv") || m.Contains("hub"))
            return true;

        // Known audio-only Cast families (speakers, cast groups).
        return !(m.Contains("audio") || m.Contains("speaker") || m.Contains("group")
                 || m.Contains("home") || m.Contains("nest") || m.Contains("mini"));
    }

    /// <summary>Media[0].Part[0].key — the streamable part path (e.g. /library/parts/123/file.mkv).</summary>
    private static string? FirstPartKey(JsonElement episode)
    {
        var media = GetArray(episode, "Media");
        if (media is null) return null;
        foreach (var m in media.Value.EnumerateArray())
        {
            var parts = GetArray(m, "Part");
            if (parts is null) continue;
            foreach (var p in parts.Value.EnumerateArray())
            {
                var key = GetStringCI(p, "key");
                if (!string.IsNullOrWhiteSpace(key))
                    return key;
            }
        }
        return null;
    }

    private static string ContentTypeFor(string partKey)
    {
        var ext = System.IO.Path.GetExtension(partKey)?.ToLowerInvariant();
        return ext switch
        {
            ".mkv" => "video/x-matroska",
            ".m3u8" => "application/x-mpegURL",
            ".webm" => "video/webm",
            ".avi" => "video/x-msvideo",
            _ => "video/mp4",
        };
    }

    // ── Plex connection + JSON helpers ─────────────────────────────────────────────────────────

    private async Task<(bool ok, string host, string token)> GetPlexConnectionAsync()
    {
        var hostEntry = await propertyBagRepository.GetByKeyAsync(SettingsKeys.PlexHost);
        var tokenEntry = await propertyBagRepository.GetByKeyAsync(SettingsKeys.PlexToken);
        var host = (hostEntry?.Value ?? string.Empty).Trim().TrimEnd('/');
        var token = tokenEntry?.Value ?? string.Empty;
        var ok = !string.IsNullOrWhiteSpace(host) && !string.IsNullOrWhiteSpace(token);
        return (ok, host, token);
    }

    private static HttpClient CreatePlexClient(string token)
    {
        var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        httpClient.DefaultRequestHeaders.Add("X-Plex-Token", token);
        httpClient.DefaultRequestHeaders.Add("X-Plex-Client-Identifier", Constants.PlexClientIdentifier);
        httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return httpClient;
    }

    private static bool TryGetCI(JsonElement obj, string name, out JsonElement value)
    {
        if (obj.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in obj.EnumerateObject())
            {
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = p.Value;
                    return true;
                }
            }
        }
        value = default;
        return false;
    }

    private static JsonElement? GetArray(JsonElement obj, params string[] names)
    {
        foreach (var name in names)
        {
            if (TryGetCI(obj, name, out var v) && v.ValueKind == JsonValueKind.Array)
                return v;
        }
        return null;
    }

    private static string? GetStringCI(JsonElement element, string name)
    {
        if (!TryGetCI(element, name, out var v))
            return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            _ => null,
        };
    }

    private static long GetLongCI(JsonElement element, string name)
    {
        if (!TryGetCI(element, name, out var v))
            return 0;
        return v.ValueKind switch
        {
            JsonValueKind.Number when v.TryGetInt64(out var n) => n,
            JsonValueKind.String when long.TryParse(v.GetString(), out var n) => n,
            _ => 0,
        };
    }
}
