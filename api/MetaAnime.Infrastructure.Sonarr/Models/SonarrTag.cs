#nullable disable
using System.Text.Json.Serialization;

namespace MetaAnime.Infrastructure.SonarrAPIs.Sonarr.Models;

public sealed class SonarrTag
{
    [JsonPropertyName("label")]
    public string Label { get; set; }
    [JsonPropertyName("id")]
    public int Id { get; set; }
}
