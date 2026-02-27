#nullable disable
using System.Text.Json.Serialization;

namespace MetaAnime.Infrastructure.SonarrAPIs.Sonarr.Models;

public partial class Image
{
    [JsonPropertyName("coverType")]
    public string CoverType { get; set; }
    [JsonPropertyName("url")]
    public string Url { get; set; }
    [JsonPropertyName("remoteUrl")]
    public string RemoteUrl { get; set; }
}
