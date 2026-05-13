namespace MetaAnime.Application.Contracts;

/// <summary>VA profile data fetched on-demand from Jikan and cached in DB.</summary>
public sealed record VoiceActorProfileResponse(
    int PersonId,
    string Name,
    string? FamilyName,
    string? GivenName,
    string? ImageUrl,
    /// <summary>Age computed from Birthday at query time. Null if birthday unknown.</summary>
    int? Age,
    string? BloodType,
    decimal? HeightCm,
    decimal? WeightKg,
    /// <summary>ISO date string (YYYY-MM-DD) or null.</summary>
    string? Birthday
);
