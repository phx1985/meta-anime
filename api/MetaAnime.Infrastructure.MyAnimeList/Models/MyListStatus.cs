#nullable disable
using System.Text.Json.Serialization;

namespace MetaAnime.Infrastructure.MyAnimeList.Models;

public partial class MyListStatus
{
    [JsonPropertyName("is_rewatching")]
    public bool IsRewatching { get; set; }
    [JsonPropertyName("num_episodes_watched")]
    public int NumEpisodesWatched { get; set; }
    [JsonPropertyName("score")]
    public int Score { get; set; }
    [JsonPropertyName("status")]
    public string Status { get; set; }
    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; }
    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }
    [JsonPropertyName("finish_date")]
    public string FinishDate { get; set; }
}
