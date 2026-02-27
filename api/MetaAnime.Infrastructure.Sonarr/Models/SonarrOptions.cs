using System.Text.Json.Serialization;

namespace MetaAnime.Infrastructure.SonarrAPIs.Sonarr.Models;

public sealed class SonarrRootFolder
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;
    [JsonPropertyName("freeSpace")]
    public long FreeSpace { get; set; }
}

public sealed class SonarrQualityProfile
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}
