using System.Text.Json.Serialization;

namespace MetaAnime.Application.Services.AniList;

/// <summary>
/// DTOs for the AniList GraphQL API (POST https://graphql.anilist.co). Every property carries
/// an explicit <see cref="JsonPropertyNameAttribute"/> matching the GraphQL wire names exactly
/// (field names are emitted as declared in the query, not camelCase-mapped).
/// </summary>

/// <summary>GraphQL response envelope: <c>{ "data": …, "errors": […] }</c>.</summary>
public sealed class AniListGraphQlEnvelope<T>
{
    [JsonPropertyName("data")]
    public T? Data { get; set; }

    [JsonPropertyName("errors")]
    public List<AniListGraphQlError>? Errors { get; set; }
}

public sealed class AniListGraphQlError
{
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("status")]
    public int? Status { get; set; }
}

// ── Cast query payload (data) ─────────────────────────────────────────────────

public sealed class AniListMediaCastData
{
    [JsonPropertyName("Media")]
    public AniListMedia? Media { get; set; }
}

public sealed class AniListMedia
{
    [JsonPropertyName("idMal")]
    public int? IdMal { get; set; }

    [JsonPropertyName("title")]
    public AniListTitle? Title { get; set; }

    [JsonPropertyName("characters")]
    public AniListCharacterConnection? Characters { get; set; }
}

public sealed class AniListTitle
{
    [JsonPropertyName("romaji")]
    public string? Romaji { get; set; }

    [JsonPropertyName("english")]
    public string? English { get; set; }
}

public sealed class AniListCharacterConnection
{
    [JsonPropertyName("pageInfo")]
    public AniListPageInfo? PageInfo { get; set; }

    [JsonPropertyName("edges")]
    public List<AniListCharacterEdge>? Edges { get; set; }
}

public sealed class AniListPageInfo
{
    [JsonPropertyName("hasNextPage")]
    public bool HasNextPage { get; set; }
}

public sealed class AniListCharacterEdge
{
    /// <summary>"MAIN" | "SUPPORTING" | "BACKGROUND".</summary>
    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("node")]
    public AniListCharacter? Node { get; set; }

    /// <summary>May arrive null (not merely empty) on some pages — always coalesce.</summary>
    [JsonPropertyName("voiceActors")]
    public List<AniListVoiceActor>? VoiceActors { get; set; }
}

public sealed class AniListCharacter
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public AniListName? Name { get; set; }

    [JsonPropertyName("image")]
    public AniListImage? Image { get; set; }
}

public sealed class AniListVoiceActor
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public AniListName? Name { get; set; }

    /// <summary>Language name ("Japanese", "English", …); may be null.</summary>
    [JsonPropertyName("languageV2")]
    public string? LanguageV2 { get; set; }

    [JsonPropertyName("image")]
    public AniListImage? Image { get; set; }

    [JsonPropertyName("dateOfBirth")]
    public AniListFuzzyDate? DateOfBirth { get; set; }

    [JsonPropertyName("bloodType")]
    public string? BloodType { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

public sealed class AniListName
{
    [JsonPropertyName("full")]
    public string? Full { get; set; }

    [JsonPropertyName("native")]
    public string? Native { get; set; }

    /// <summary>Explicit given name when the upstream supplies one; else split fallback applies.</summary>
    [JsonPropertyName("given_name")]
    public string? GivenName { get; set; }

    /// <summary>Explicit family name when the upstream supplies one; else split fallback applies.</summary>
    [JsonPropertyName("family_name")]
    public string? FamilyName { get; set; }
}

public sealed class AniListImage
{
    [JsonPropertyName("large")]
    public string? Large { get; set; }

    [JsonPropertyName("medium")]
    public string? Medium { get; set; }

    /// <summary>Null-safe image extraction: large with fallback to medium.</summary>
    public string? GetImageUrl() =>
        !string.IsNullOrWhiteSpace(Large) ? Large
        : !string.IsNullOrWhiteSpace(Medium) ? Medium
        : null;
}

public sealed class AniListFuzzyDate
{
    [JsonPropertyName("year")]
    public int? Year { get; set; }

    [JsonPropertyName("month")]
    public int? Month { get; set; }

    [JsonPropertyName("day")]
    public int? Day { get; set; }
}

// ── Staff profile query payload (data) ────────────────────────────────────────

public sealed class AniListStaffData
{
    [JsonPropertyName("Staff")]
    public AniListStaff? Staff { get; set; }
}

public sealed class AniListStaff
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public AniListName? Name { get; set; }

    [JsonPropertyName("image")]
    public AniListImage? Image { get; set; }

    [JsonPropertyName("dateOfBirth")]
    public AniListFuzzyDate? DateOfBirth { get; set; }

    [JsonPropertyName("bloodType")]
    public string? BloodType { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}
