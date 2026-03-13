using System.Text.Json.Serialization;

namespace MetaAnime.Application.Services.BotKun;

/// <summary>
/// JSON schema extracted by the AI intent pass. All fields are optional;
/// null means "not specified by the user" and the filter is omitted.
/// </summary>
internal sealed record BotKunSearchIntent(
    [property: JsonPropertyName("type")]          string         Type,
    [property: JsonPropertyName("title")]         string?        Title,
    [property: JsonPropertyName("genres")]        string[]?      Genres,
    [property: JsonPropertyName("airingStatus")]  string?        AiringStatus,
    [property: JsonPropertyName("watchStatus")]   string?        WatchStatus,
    [property: JsonPropertyName("mediaType")]     string?        MediaType,
    [property: JsonPropertyName("demographic")]   string?        Demographic,
    [property: JsonPropertyName("themes")]        string[]?      Themes,
    [property: JsonPropertyName("minYear")]       int?           MinYear,
    [property: JsonPropertyName("maxYear")]       int?           MaxYear,
    [property: JsonPropertyName("ageRating")]     string[]?      AgeRating,
    [property: JsonPropertyName("page")]          int            Page = 1);
