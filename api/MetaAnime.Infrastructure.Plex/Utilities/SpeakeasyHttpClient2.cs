using LukeHagar.PlexAPI.SDK.Utils;
using System.Net.Http.Headers;

namespace MetaAnime.Infrastructure.Plex.Utilities
{
    /// <summary>
    /// This class is a fix for the SpeakeasyHttpClient class in the PlexAPI SDK that doesn't natively handle request as JSON
    /// </summary>
    public sealed class SpeakeasyHttpClient2 : SpeakeasyHttpClient, IDisposable
    {
        public SpeakeasyHttpClient2()
        {
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        public void Dispose() => httpClient.Dispose();
    }
}
