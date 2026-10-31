namespace MetaAnime.Domain.Entities;

/// <summary>
/// Caches character and voice actor data for a specific anime, fetched from the AniList GraphQL API.
/// CharacterId/VaPersonId are AniList character/staff IDs (one-time ID-space migration clears older MAL-ID rows).
/// </summary>
public sealed class AnimeCast
{
    public Guid Id { get; set; }
    /// <summary>MAL anime ID this cast entry belongs to.</summary>
    public required string AnimeMalId { get; set; }
    public required int CharacterId { get; set; }
    public required string CharacterName { get; set; }
    /// <summary>Main or Supporting.</summary>
    public required string CharacterRole { get; set; }
    public string? CharacterImageUrl { get; set; }
    public required int VaPersonId { get; set; }
    public required string VaName { get; set; }
    public string? VaImageUrl { get; set; }
    /// <summary>Language of the VA (e.g. "Japanese").</summary>
    public required string Language { get; set; }
    public DateTime SyncedAt { get; set; }
}
