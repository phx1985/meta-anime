#nullable disable
using System.Text.Json.Serialization;

namespace MetaAnime.Infrastructure.MyAnimeList.Models;

public partial class MainPicture
{
    [JsonPropertyName("large")]
    public string Large { get; set; }
    [JsonPropertyName("medium")]
    public string Medium { get; set; }
}
