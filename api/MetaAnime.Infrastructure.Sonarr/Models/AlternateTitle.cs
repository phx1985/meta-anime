#nullable disable
using System.Text.Json.Serialization;

namespace MetaAnime.Infrastructure.SonarrAPIs.Sonarr.Models
{
    public partial class AlternateTitle
    {
        [JsonPropertyName("title")]
        public string Title { get; set; }
        [JsonPropertyName("sceneSeasonNumber")]
        public int SceneSeasonNumber { get; set; }
        [JsonPropertyName("seasonNumber")]
        public int SeasonNumber { get; set; }
        [JsonPropertyName("comment")]
        public string Comment { get; set; }
    }
}
