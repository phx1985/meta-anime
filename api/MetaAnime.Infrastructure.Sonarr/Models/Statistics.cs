#nullable disable
using System.Text.Json.Serialization;

namespace MetaAnime.Infrastructure.SonarrAPIs.Sonarr.Models
{
    public partial class Statistics
    {
        [JsonPropertyName("episodeFileCount")]
        public int EpisodeFileCount { get; set; }
        [JsonPropertyName("episodeCount")]
        public int EpisodeCount { get; set; }
        [JsonPropertyName("totalEpisodeCount")]
        public int TotalEpisodeCount { get; set; }
        [JsonPropertyName("sizeOnDisk")]
        public object SizeOnDisk { get; set; }
        [JsonPropertyName("releaseGroups")]
        public List<string> ReleaseGroups { get; set; }
        [JsonPropertyName("percentOfEpisodes")]
        public double PercentOfEpisodes { get; set; }
        [JsonPropertyName("previousAiring")]
        public DateTime? PreviousAiring { get; set; }
        [JsonPropertyName("nextAiring")]
        public DateTime? NextAiring { get; set; }
        [JsonPropertyName("seasonCount")]
        public int SeasonCount { get; set; }
    }

}
