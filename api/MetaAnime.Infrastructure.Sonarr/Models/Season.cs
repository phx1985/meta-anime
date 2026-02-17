#nullable disable
using System.Text.Json.Serialization;

namespace MetaAnime.Infrastructure.SonarrAPIs.Sonarr.Models
{
    public partial class Season
    {
        [JsonPropertyName("seasonNumber")]
        public int SeasonNumber { get; set; }
        [JsonPropertyName("monitored")]
        public bool Monitored { get; set; }
        [JsonPropertyName("statistics")]
        public Statistics Statistics { get; set; }
    }
}
