#nullable disable
using System.Text.Json.Serialization;

namespace MetaAnime.Infrastructure.SonarrAPIs.Sonarr.Models
{
    public partial class SonarrSeries
    {
        [JsonPropertyName("title")]
        public string Title { get; set; }
        [JsonPropertyName("alternateTitles")]
        public List<AlternateTitle> AlternateTitles { get; set; }
        [JsonPropertyName("sortTitle")]
        public string SortTitle { get; set; }
        [JsonPropertyName("status")]
        public string Status { get; set; }
        [JsonPropertyName("ended")]
        public bool Ended { get; set; }
        [JsonPropertyName("overview")]
        public string Overview { get; set; }
        [JsonPropertyName("previousAiring")]
        public DateTime PreviousAiring { get; set; }
        [JsonPropertyName("network")]
        public string Network { get; set; }
        [JsonPropertyName("airTime")]
        public string AirTime { get; set; }
        [JsonPropertyName("images")]
        public List<Image> Images { get; set; }
        [JsonPropertyName("originalLanguage")]
        public OriginalLanguage OriginalLanguage { get; set; }
        [JsonPropertyName("seasons")]
        public List<Season> Seasons { get; set; }
        [JsonPropertyName("year")]
        public int Year { get; set; }
        [JsonPropertyName("path")]
        public string Path { get; set; }
        [JsonPropertyName("qualityProfileId")]
        public int QualityProfileId { get; set; }
        [JsonPropertyName("seasonFolder")]
        public bool SeasonFolder { get; set; }
        [JsonPropertyName("monitored")]
        public bool Monitored { get; set; }
        [JsonPropertyName("monitorNewItems")]
        public string MonitorNewItems { get; set; }
        [JsonPropertyName("useSceneNumbering")]
        public bool UseSceneNumbering { get; set; }
        [JsonPropertyName("runtime")]
        public int Runtime { get; set; }
        [JsonPropertyName("tvdbId")]
        public int TvdbId { get; set; }
        [JsonPropertyName("tvRageId")]
        public int TvRageId { get; set; }
        [JsonPropertyName("tvMazeId")]
        public int TvMazeId { get; set; }
        [JsonPropertyName("tmdbId")]
        public int TmdbId { get; set; }
        [JsonPropertyName("firstAired")]
        public DateTime FirstAired { get; set; }
        [JsonPropertyName("lastAired")]
        public DateTime LastAired { get; set; }
        [JsonPropertyName("seriesType")]
        public string SeriesType { get; set; }
        [JsonPropertyName("cleanTitle")]
        public string CleanTitle { get; set; }
        [JsonPropertyName("imdbId")]
        public string ImdbId { get; set; }
        [JsonPropertyName("titleSlug")]
        public string TitleSlug { get; set; }
        [JsonPropertyName("rootFolderPath")]
        public string RootFolderPath { get; set; }
        [JsonPropertyName("certification")]
        public string Certification { get; set; }
        [JsonPropertyName("genres")]
        public List<string> Genres { get; set; }
        [JsonPropertyName("tags")]
        public List<int> Tags { get; set; }
        [JsonPropertyName("added")]
        public DateTime Added { get; set; }
        [JsonPropertyName("ratings")]
        public Ratings Ratings { get; set; }
        [JsonPropertyName("statistics")]
        public Statistics Statistics { get; set; }
        [JsonPropertyName("languageProfileId")]
        public int LanguageProfileId { get; set; }
        [JsonPropertyName("id")]
        public int Id { get; set; }
        [JsonPropertyName("nextAiring")]
        public DateTime? NextAiring { get; set; }
    }

}
