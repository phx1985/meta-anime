using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Domain.Entities;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace MetaAnime.Application.Services;

/// <summary>
/// Fetches voice actor profiles from the Jikan REST API (https://api.jikan.moe/v4)
/// and persists them in the <see cref="VoiceActor"/> table with a 30-day cache TTL.
/// </summary>
public sealed class JikanPersonService(
    IHttpClientFactory httpClientFactory,
    IVoiceActorRepository voiceActorRepository,
    ILogger<JikanPersonService> logger)
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromDays(30);

    /// <summary>
    /// Returns the cached VA profile if fresh, otherwise fetches from Jikan and persists it.
    /// Returns null if Jikan returns 404 or the request fails.
    /// </summary>
    public async Task<VoiceActor?> GetOrFetchAsync(int personId)
    {
        var cached = await voiceActorRepository.GetByPersonIdAsync(personId);
        if (cached is not null && DateTime.UtcNow - cached.CachedAt < CacheTtl)
            return cached;

        logger.LogInformation(Constants.LogMessages.JikanPersonService.Fetching, personId);

        var client = httpClientFactory.CreateClient("Jikan");
        JikanPersonResponse? jikan;
        try
        {
            jikan = await client.GetFromJsonAsync<JikanPersonResponse>($"people/{personId}");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, Constants.LogMessages.JikanPersonService.FetchFailed, personId);
            return cached; // Return stale cache rather than null if we have one
        }

        if (jikan?.Data is null)
            return cached;

        var d = jikan.Data;
        var va = new VoiceActor
        {
            PersonId  = personId,
            Name      = d.Name ?? string.Empty,
            FamilyName = d.FamilyName,
            GivenName  = d.GivenName,
            ImageUrl   = d.Images?.Jpg?.ImageUrl,
            Birthday   = d.Birthday,
            BloodType  = d.BloodType,
            HeightCm   = ParseMeasurement(d.Height),
            WeightKg   = ParseMeasurement(d.Weight),
            CachedAt   = DateTime.UtcNow,
        };

        await voiceActorRepository.UpsertAsync(va);
        return va;
    }

    /// <summary>Parses Jikan's free-text measurement fields like "172 cm" or "60 kg".</summary>
    private static decimal? ParseMeasurement(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        // Strip known units and whitespace then parse the numeric part
        var numeric = text
            .Replace("cm", "", StringComparison.OrdinalIgnoreCase)
            .Replace("kg", "", StringComparison.OrdinalIgnoreCase)
            .Trim();
        return decimal.TryParse(numeric, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    // ── Jikan response models (private, not exposed outside this service) ──────

    private sealed class JikanPersonResponse
    {
        [JsonPropertyName("data")]
        public JikanPersonData? Data { get; set; }
    }

    private sealed class JikanPersonData
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
        [JsonPropertyName("family_name")]
        public string? FamilyName { get; set; }
        [JsonPropertyName("given_name")]
        public string? GivenName { get; set; }
        [JsonPropertyName("images")]
        public JikanImages? Images { get; set; }
        [JsonPropertyName("birthday")]
        public string? Birthday { get; set; }
        [JsonPropertyName("blood_type")]
        public string? BloodType { get; set; }
        [JsonPropertyName("height")]
        public string? Height { get; set; }
        [JsonPropertyName("weight")]
        public string? Weight { get; set; }
    }

    private sealed class JikanImages
    {
        [JsonPropertyName("jpg")]
        public JikanJpg? Jpg { get; set; }
    }

    private sealed class JikanJpg
    {
        [JsonPropertyName("image_url")]
        public string? ImageUrl { get; set; }
    }
}
