using System.Text.Json.Serialization;

namespace MetaAnime.Infrastructure.MyAnimeList.Models;

public partial class MalAnime
{
    [JsonPropertyName("alternative_titles")]
    public AlternativeTitles? AlternativeTitles { get; set; }
    [JsonPropertyName("end_date")]
    public string? EndDate { get; set; }
    [JsonPropertyName("genres")]
    public List<Genre>? Genres { get; set; }
    [JsonPropertyName("id")]
    public int Id { get; set; }
    [JsonPropertyName("main_picture")]
    public MainPicture? MainPicture { get; set; }
    [JsonPropertyName("mean")]
    public double Mean { get; set; }
    [JsonPropertyName("media_type")]
    public string? MediaType { get; set; }
    [JsonPropertyName("my_list_status")]
    public MyListStatus? MyListStatus { get; set; }
    [JsonPropertyName("nsfw")]
    public string? Nsfw { get; set; }
    [JsonPropertyName("popularity")]
    public int Popularity { get; set; }
    [JsonPropertyName("rank")]
    public int Rank { get; set; }
    [JsonPropertyName("start_date")]
    public string? StartDate { get; set; }
    [JsonPropertyName("start_season")]
    public MalStartSeason? StartSeason { get; set; }
    [JsonPropertyName("status")]
    public string? Status { get; set; }
    [JsonPropertyName("synopsis")]
    public string? Synopsis { get; set; }
    [JsonPropertyName("title")]
    public string? Title { get; set; }
    [JsonPropertyName("num_episodes")]
    public int NumEpisodes { get; set; }

    [JsonPropertyName("related_anime")]
    public List<RelatedAnimeEdge>? RelatedAnime { get; set; }

    [JsonPropertyName("trailer")]
    public MalTrailer? Trailer { get; set; }

    [JsonPropertyName("pictures")]
    public List<MalPicture>? Pictures { get; set; }
}

public sealed class MalTrailer
{
    [JsonPropertyName("youtube_id")]
    public string? YoutubeId { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

public sealed class MalPicture
{
    [JsonPropertyName("medium")]
    public string? Medium { get; set; }

    [JsonPropertyName("large")]
    public string? Large { get; set; }
}

public sealed class MalStartSeason
{
    [JsonPropertyName("year")]
    public int Year { get; set; }
    [JsonPropertyName("season")]
    public string? Season { get; set; }
}
