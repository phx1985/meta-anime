using LukeHagar.PlexAPI.SDK.Utils;
using System.Net.Http.Headers;

namespace MetaAnime.Infrastructure.Plex.Utilities;

/// <summary>
/// This class is a fix for the SpeakeasyHttpClient class in the PlexAPI SDK that doesn't natively handle request as JSON
/// </summary>
public sealed class SpeakeasyHttpClient2 : SpeakeasyHttpClient, IDisposable
{
    private bool _disposed;

    public SpeakeasyHttpClient2(string? token = null)
    {
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        // Ensure X-Plex-Token is always sent to the local Plex server on every request.
        if (!string.IsNullOrWhiteSpace(token))
            httpClient.DefaultRequestHeaders.Add("X-Plex-Token", token);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        httpClient.Dispose();
        _disposed = true;
    }
}
