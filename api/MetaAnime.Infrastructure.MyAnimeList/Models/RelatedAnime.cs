using System.Text.Json.Serialization;

namespace MetaAnime.Infrastructure.MyAnimeList.Models;

public sealed class RelatedAnimeEdge
{
    [JsonPropertyName("node")]
    public RelatedAnimeNode? Node { get; set; }

    [JsonPropertyName("relation_type")]
    public string? RelationType { get; set; }

    [JsonPropertyName("relation_type_formatted")]
    public string? RelationTypeFormatted { get; set; }
}

public sealed class RelatedAnimeNode
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("main_picture")]
    public MainPicture? MainPicture { get; set; }

    [JsonPropertyName("media_type")]
    public string? MediaType { get; set; }
}
