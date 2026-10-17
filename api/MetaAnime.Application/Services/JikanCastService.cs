using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Domain.Entities;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace MetaAnime.Application.Services;

/// <summary>
/// Fetches anime cast (characters + voice actors) from the Jikan REST API
/// (https://api.jikan.moe/v4/anime/{id}/characters) — no authentication required.
/// </summary>
public sealed class JikanCastService(
    IHttpClientFactory httpClientFactory,
    IAnimeCastRepository animeCastRepository,
    ILogger<JikanCastService> logger)
{
    /// <summary>
    /// Returns immediately if cast data already exists for <paramref name="malId"/>.
    /// Otherwise fetches from Jikan, persists, and returns.
    /// Returns false when the Jikan fetch fails (caller decides how to surface this).
    /// </summary>
    public async Task<bool> EnsureAsync(string malId)
    {
        var existing = await animeCastRepository.GetByAnimeMalIdAsync(malId);
        if (existing.Any())
            return true;

        logger.LogInformation(Constants.LogMessages.CastSyncJob.Starting, malId);

        var client = httpClientFactory.CreateClient("Jikan");
        JikanCharactersResponse? response;
        try
        {
            response = await client.GetFromJsonAsync<JikanCharactersResponse>($"anime/{malId}/characters");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, Constants.LogMessages.CastSyncJob.Error, malId);
            return false;
        }

        if (response?.Data is null || response.Data.Count == 0)
        {
            logger.LogWarning(Constants.LogMessages.CastSyncJob.Skipped, malId);
            return false;
        }

        var now = DateTime.UtcNow;
        var entries = response.Data
            .Where(c => c.Character?.MalId > 0)
            .SelectMany(c => (c.VoiceActors ?? [])
                .Where(va => va.Person?.MalId > 0)
                .Select(va => new AnimeCast
                {
                    AnimeMalId        = malId,
                    CharacterId       = c.Character!.MalId,
                    CharacterName     = c.Character.Name ?? string.Empty,
                    CharacterRole     = c.Role ?? string.Empty,
                    CharacterImageUrl = c.Character.Images?.Jpg?.ImageUrl,
                    VaPersonId        = va.Person!.MalId,
                    VaName            = va.Person.Name ?? string.Empty,
                    VaImageUrl        = va.Person.Images?.Jpg?.ImageUrl,
                    Language          = va.Language ?? string.Empty,
                    SyncedAt          = now,
                }))
            // Jikan may return multiple VAs per character+language — keep only one
            .GroupBy(e => (e.CharacterId, e.Language))
            .Select(g => g.First())
            .ToList();

        await animeCastRepository.BulkUpsertAsync(entries);
        logger.LogInformation(Constants.LogMessages.CastSyncJob.Succeeded, malId, entries.Count);
        return true;
    }

    // ── Jikan response models ────────────────────────────────────────────────────

    private sealed class JikanCharactersResponse
    {
        [JsonPropertyName("data")]
        public List<JikanCharacterEntry>? Data { get; set; }
    }

    private sealed class JikanCharacterEntry
    {
        [JsonPropertyName("character")]
        public JikanCharacter? Character { get; set; }

        [JsonPropertyName("role")]
        public string? Role { get; set; }

        [JsonPropertyName("voice_actors")]
        public List<JikanVoiceActorEntry>? VoiceActors { get; set; }
    }

    private sealed class JikanCharacter
    {
        [JsonPropertyName("mal_id")]
        public int MalId { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("images")]
        public JikanImages? Images { get; set; }
    }

    private sealed class JikanVoiceActorEntry
    {
        [JsonPropertyName("person")]
        public JikanPerson? Person { get; set; }

        [JsonPropertyName("language")]
        public string? Language { get; set; }
    }

    private sealed class JikanPerson
    {
        [JsonPropertyName("mal_id")]
        public int MalId { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("images")]
        public JikanImages? Images { get; set; }
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
