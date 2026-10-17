using System.Text.Json.Serialization;

namespace MetaAnime.Infrastructure.MyAnimeList.Models;

public sealed class MalCharacterEdge
{
    [JsonPropertyName("node")]
    public MalCharacterNode? Node { get; set; }

    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("voice_actors")]
    public List<MalVoiceActorEdge> VoiceActors { get; set; } = [];
}

public sealed class MalCharacterNode
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }

    [JsonPropertyName("main_picture")]
    public MainPicture? MainPicture { get; set; }
}

public sealed class MalVoiceActorEdge
{
    [JsonPropertyName("node")]
    public MalPersonNode? Node { get; set; }

    [JsonPropertyName("language")]
    public string? Language { get; set; }
}

public sealed class MalPersonNode
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }

    [JsonPropertyName("main_picture")]
    public MainPicture? MainPicture { get; set; }
}

/// <summary>Response shape for a single anime's characters list.</summary>
public sealed class MalAnimeCharactersResponse
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("characters")]
    public List<MalCharacterEdge> Characters { get; set; } = [];
}
