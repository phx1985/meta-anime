namespace MetaAnime.Core.Models;

/// <summary>Domain model for a character's voice actor from a MAL cast response.</summary>
public sealed record MalVoiceActorInfo(
    int PersonId,
    string Name,
    string? ImageUrl,
    string Language
);

/// <summary>Domain model for a single character from a MAL cast response.</summary>
public sealed record MalCharacterInfo(
    int CharacterId,
    string Name,
    string? ImageUrl,
    string Role,
    IReadOnlyList<MalVoiceActorInfo> VoiceActors
);

/// <summary>Domain model returned by <see cref="MetaAnime.Core.Interfaces.Services.IMalAnimeSourceService{T}.GetAnimeCharactersAsync"/>.</summary>
public sealed record MalAnimeCharactersResult(
    string AnimeMalId,
    IReadOnlyList<MalCharacterInfo> Characters
);
