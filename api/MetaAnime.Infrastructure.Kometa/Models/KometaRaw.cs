#nullable disable
using MetaAnime.Infrastructure.Kometa.Infrastructure.Kometa.Utilities.Json.Converters;
using System.Text.Json.Serialization;

namespace MetaAnime.Infrastructure.Kometa.Models;

public sealed class KometaRaw
{
    [JsonIgnore]
    public string AniDbId { get; set; }

    [JsonPropertyName("tvdb_id")]
    [JsonConverter(typeof(KometaIdsMixedTypeToStringConverter))]
    public string[] TvdbIds { get; set; }

    [JsonPropertyName("imdb_id")]
    [JsonConverter(typeof(KometaIdsMixedTypeToStringConverter))]
    public string[] ImdbIds { get; set; }

    [JsonPropertyName("mal_id")]
    [JsonConverter(typeof(KometaIdsMixedTypeToStringConverter))]
    public string[] MalIds { get; set; }

    [JsonPropertyName("anilist_id")]
    [JsonConverter(typeof(KometaIdsMixedTypeToStringConverter))]
    public string[] AnilistIds { get; set; }

    [JsonPropertyName("tmdb_movie_id")]
    [JsonConverter(typeof(KometaIdsMixedTypeToStringConverter))]
    public string[] TmdbMovieIds { get; set; }

    [JsonPropertyName("tmdb_show_id")]
    [JsonConverter(typeof(KometaIdsMixedTypeToStringConverter))]
    public string[] TmdbShowIds { get; set; }

    [JsonPropertyName("tvdb_season")]
    public int? TvdbSeason { get; set; }

    [JsonPropertyName("tvdb_epoffset")]
    public int? TvdbEpOffset { get; set; }
}
