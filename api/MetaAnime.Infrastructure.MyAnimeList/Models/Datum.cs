#nullable disable
using System.Text.Json.Serialization;

namespace MetaAnime.Infrastructure.MyAnimeList.Models;

public partial class Datum
{
    [JsonPropertyName("node")]
    public MalAnime Node { get; set; }
}
