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
    [JsonPropertyName("width")]
    public int Width { get; set; }
    [JsonPropertyName("height")]
    public int Height { get; set; }
    [JsonPropertyName("resolution")]
    public string Resolution { get; set; }

    /// <summary>Maps a pixel height to a human-readable resolution label.</summary>
    public static string ToResolutionLabel(int height) => height switch
    {
        >= 2160 => "4K",
        >= 1080 => "1080p",
        >= 720  => "720p",
        >= 480  => "480p",
        _       => "SD"
    };

    /// <summary>
    /// Primary entry point for resolution detection.
    /// Prefers parsing from the <paramref name="resolution"/> string (e.g. "1920x1080" → 1080)
    /// then falls back to the <paramref name="height"/> int field.
    /// </summary>
    public static string ParseResolutionLabel(string resolution, int height)
    {
        if (!string.IsNullOrWhiteSpace(resolution))
        {
            var xIdx = resolution.IndexOf('x', StringComparison.OrdinalIgnoreCase);
            if (xIdx > 0 && int.TryParse(resolution.AsSpan(xIdx + 1), out var parsedHeight) && parsedHeight > 0)
                return ToResolutionLabel(parsedHeight);
        }
        if (height > 0)
            return ToResolutionLabel(height);
        return "Unknown";
    }
}
