#nullable disable
using System.Text.Json.Serialization;

namespace MetaAnime.Infrastructure.MyAnimeList.Models;

public partial class MalResult
{
    [JsonPropertyName("data")]
    public List<Datum> Data { get; set; }
    [JsonPropertyName("paging")]
    public Paging Paging { get; set; }
}
