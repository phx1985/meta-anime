#nullable disable
using System.Text.Json.Serialization;

namespace MetaAnime.Infrastructure.MyAnimeList.Models;

public partial class AlternativeTitles
{
    [JsonPropertyName("en")]
    public string En { get; set; }
    [JsonPropertyName("ja")]
    public string Ja { get; set; }
    [JsonPropertyName("synonyms")]
    public List<string> Synonyms { get; set; }
}

