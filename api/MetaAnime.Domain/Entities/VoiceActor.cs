namespace MetaAnime.Domain.Entities;

/// <summary>
/// Caches rich voice actor profile data from the AniList GraphQL API (30-day TTL).
/// </summary>
public sealed class VoiceActor
{
    /// <summary>AniList staff ID (primary key).</summary>
    public required int PersonId { get; set; }
    public required string Name { get; set; }
    public string? FamilyName { get; set; }
    public string? GivenName { get; set; }
    public string? ImageUrl { get; set; }
    public string? Birthday { get; set; }
    public string? BloodType { get; set; }
    /// <summary>Height in centimetres. AniList carries no height — always stored as null.</summary>
    public decimal? HeightCm { get; set; }
    /// <summary>Weight in kilograms. AniList carries no weight — always stored as null.</summary>
    public decimal? WeightKg { get; set; }
    /// <summary>When this profile was last fetched from AniList.</summary>
    public DateTime CachedAt { get; set; }
}
