#nullable disable
using System.Text.Json.Serialization;

namespace MetaAnime.Infrastructure.MyAnimeList.Models;

public sealed class MalError
{
    /// <summary>MAL's machine-readable error code (JSON <c>error</c>), e.g. "bad_request".</summary>
    [JsonPropertyName("error")]
    public string Message { get; set; }

    /// <summary>MAL's human-readable explanation (JSON <c>message</c>), e.g. "invalid q".</summary>
    [JsonPropertyName("message")]
    public string Detail { get; set; }

    public override string ToString() => Message;
}
