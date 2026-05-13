namespace MetaAnime.Domain.Entities;

/// <summary>
/// Caches rich voice actor profile data from the Jikan API (30-day TTL).
/// </summary>
public sealed class VoiceActor
{
    /// <summary>MAL person ID (primary key).</summary>
    public required int PersonId { get; set; }
    public required string Name { get; set; }
    public string? FamilyName { get; set; }
    public string? GivenName { get; set; }
    public string? ImageUrl { get; set; }
    public string? Birthday { get; set; }
    public string? BloodType { get; set; }
    /// <summary>Height in centimetres, parsed from Jikan's free-text field.</summary>
    public decimal? HeightCm { get; set; }
    /// <summary>Weight in kilograms, parsed from Jikan's free-text field.</summary>
    public decimal? WeightKg { get; set; }
    /// <summary>When this profile was last fetched from Jikan.</summary>
    public DateTime CachedAt { get; set; }
}
