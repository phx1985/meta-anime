#nullable disable
using System.Text.Json.Serialization;

namespace MetaAnime.Infrastructure.MyAnimeList.Models;

public sealed class MalError
{
    [JsonPropertyName("error")]
    public string Message { get; set; }
    public override string ToString() => Message;
}
