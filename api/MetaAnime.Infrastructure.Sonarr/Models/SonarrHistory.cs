#nullable disable
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MetaAnime.Infrastructure.SonarrAPIs.Sonarr.Models;

public sealed class SonarrHistoryResponse
{
    [JsonPropertyName("records")]
    public List<SonarrHistoryRecord> Records { get; set; } = [];

    [JsonPropertyName("totalRecords")]
    public int TotalRecords { get; set; }
}

public sealed class SonarrHistoryRecord
{
    [JsonPropertyName("seriesId")]
    public int SeriesId { get; set; }

    [JsonPropertyName("episodeId")]
    public int EpisodeId { get; set; }

    [JsonPropertyName("date")]
    public DateTime Date { get; set; }

    [JsonPropertyName("eventType")]
    public string EventType { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public SonarrHistoryData Data { get; set; }
}

public sealed class SonarrHistoryData
{
    /// <summary>
    /// File size in bytes. Sonarr returns this as a numeric string in most versions
    /// but may omit it for some event types. We store as JsonElement to safely
    /// handle both string and number representations.
    /// </summary>
    [JsonPropertyName("size")]
    public JsonElement? Size { get; set; }

    /// <summary>Safely parse the size value regardless of whether it's a string or number.</summary>
    public long GetSizeBytes()
    {
        if (Size is null) return 0L;
        var el = Size.Value;
        return el.ValueKind switch
        {
            JsonValueKind.Number when el.TryGetInt64(out var n) => n,
            JsonValueKind.String when long.TryParse(el.GetString(), out var s) => s,
            _ => 0L,
        };
    }
}
