#nullable disable
using System.Text.Json.Serialization;

namespace MetaAnime.Infrastructure.SonarrAPIs.Sonarr.Models;

public partial class SonarrEpisodeFile
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
    [JsonPropertyName("seriesId")]
    public int SeriesId { get; set; }
    [JsonPropertyName("seasonNumber")]
    public int SeasonNumber { get; set; }
    [JsonPropertyName("relativePath")]
    public string RelativePath { get; set; }
    [JsonPropertyName("path")]
    public string Path { get; set; }
    [JsonPropertyName("size")]
    public long Size { get; set; }
    [JsonPropertyName("dateAdded")]
    public DateTime DateAdded { get; set; }
    [JsonPropertyName("quality")]
    public Quality Quality { get; set; }
    [JsonPropertyName("mediaInfo")]
    public MediaInfo MediaInfo { get; set; }
}

public partial class Quality
{
    [JsonPropertyName("quality")]
    public QualityInfo QualityInfo { get; set; }
}

public partial class QualityInfo
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
    [JsonPropertyName("name")]
    public string Name { get; set; }
}

public partial class MediaInfo
{
    [JsonPropertyName("videoCodec")]
    public string VideoCodec { get; set; }
    [JsonPropertyName("videoBitDepth")]
    public int VideoBitDepth { get; set; }
    [JsonPropertyName("videoColourPrimaries")]
    public string VideoColourPrimaries { get; set; }
    [JsonPropertyName("audioCodec")]
    public string AudioCodec { get; set; }
    [JsonPropertyName("audioChannels")]
    public double AudioChannels { get; set; }
    [JsonPropertyName("runTime")]
    public string RunTime { get; set; }
}
