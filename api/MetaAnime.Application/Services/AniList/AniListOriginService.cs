using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace MetaAnime.Application.Services.AniList;

/// <summary>Payload of the batched country-of-origin query.</summary>
public sealed class AniListOriginPage
{
    [JsonPropertyName("Page")]
    public AniListOriginPageInner? Page { get; set; }
}

public sealed class AniListOriginPageInner
{
    [JsonPropertyName("media")]
    public List<AniListOriginMedia>? Media { get; set; }
}

public sealed class AniListOriginMedia
{
    [JsonPropertyName("idMal")]
    public int? IdMal { get; set; }

    /// <summary>ISO country the animation comes from: "JP", "CN", "KR", "TW"…</summary>
    [JsonPropertyName("countryOfOrigin")]
    public string? CountryOfOrigin { get; set; }
}

/// <summary>
/// Resolves the production country of anime by MAL id through AniList's <c>countryOfOrigin</c>.
///
/// MAL itself exposes no origin field and the Kometa mapping has none either, so a studio
/// blocklist alone can never be complete (verified: "Da Wang Rao Ming 3" is produced by
/// "Big Firebird Culture", "Luo Xiaohei" by "HMCH"). AniList is the authoritative source, and
/// its <c>Page.media(idMal_in: […])</c> filter answers 50 titles per request.
/// Results are cached for a day — an origin never changes.
/// </summary>
public sealed class AniListOriginService(
    IAniListClient anilistClient,
    IMemoryCache cache,
    ILogger<AniListOriginService> logger)
{
    private const int BatchSize = 50;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);
    private const string CacheKeyPrefix = "anilist:origin:";
    private const string Unknown = "";

    private const string OriginQuery = """
        query ($ids: [Int]) {
          Page(perPage: 50) {
            media(idMal_in: $ids, type: ANIME) {
              idMal
              countryOfOrigin
            }
          }
        }
        """;

    /// <summary>
    /// Returns MAL id → ISO country for every title AniList knows about. Missing ids are
    /// simply absent from the result (the caller falls back to its studio heuristics).
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> GetOriginsAsync(
        IEnumerable<string> malIds,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var missing = new List<int>();

        foreach (var id in malIds.Distinct())
        {
            if (!int.TryParse(id, out var malId)) continue;
            if (cache.TryGetValue(CacheKeyPrefix + malId, out string? cached) && cached is not null)
            {
                if (cached.Length > 0) result[id] = cached;
                continue;
            }
            missing.Add(malId);
        }

        foreach (var chunk in missing.Chunk(BatchSize))
        {
            var api = await anilistClient.PostQueryAsync<AniListOriginPage>(
                OriginQuery,
                new Dictionary<string, object?> { ["ids"] = chunk },
                cancellationToken);

            if (api.Status != AniListApiStatus.Success || api.Data?.Page?.Media is null)
            {
                // Never fail the caller: unknown origin simply means "do not exclude".
                logger.LogDebug("AniList origin lookup failed ({Status}) for {Count} ids", api.Status, chunk.Length);
                continue;
            }

            var answered = new HashSet<int>();
            foreach (var media in api.Data.Page.Media)
            {
                if (media.IdMal is null) continue;
                answered.Add(media.IdMal.Value);
                var country = media.CountryOfOrigin ?? Unknown;
                cache.Set(CacheKeyPrefix + media.IdMal.Value, country, CacheTtl);
                if (country.Length > 0) result[media.IdMal.Value.ToString()] = country;
            }

            // Remember the misses too, so a second page view costs nothing.
            foreach (var id in chunk.Where(id => !answered.Contains(id)))
                cache.Set(CacheKeyPrefix + id, Unknown, TimeSpan.FromHours(6));
        }

        return result;
    }
}
