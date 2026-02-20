#nullable disable
using System.Text.Json.Serialization;

namespace MetaAnime.Infrastructure.SonarrAPIs.Sonarr.Models;

public partial class Ratings
{
    [JsonPropertyName("votes")]
    public int Votes { get; set; }
    [JsonPropertyName("value")]
    public double Value { get; set; }
}
