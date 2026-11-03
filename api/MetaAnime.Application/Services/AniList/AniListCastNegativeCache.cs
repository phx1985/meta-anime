using Microsoft.Extensions.Caching.Memory;

namespace MetaAnime.Application.Services.AniList;

/// <summary>The two distinct negative answers a cast lookup can remember.</summary>
public enum AniListNegativeState
{
    /// <summary>AniList was unreachable/rate-limited — answer 503 immediately, zero network.</summary>
    ProviderUnavailable,
    /// <summary>AniList answered definitively with no cast data — answer an empty graph immediately, zero network.</summary>
    DefinitiveEmpty,
}

/// <summary>
/// Two-state negative cache for cast lookups (key <c>anilist:neg:cast:{malId}</c>, TTL 5 min)
/// plus a short-lived in-flight "loading" flag per anime. Every cache-hit path clears the
/// loading flag so a request served from cache can never leave a stale loading marker behind.
/// </summary>
public sealed class AniListCastNegativeCache(IMemoryCache cache)
{
    private static readonly TimeSpan NegativeTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan LoadingTtl = TimeSpan.FromMinutes(2);

    private static string NegativeKey(string malId) => $"anilist:neg:cast:{malId}";
    private static string LoadingKey(string malId) => $"anilist:loading:cast:{malId}";

    /// <summary>Returns the remembered negative answer (and clears the loading flag on a hit).</summary>
    public bool TryGet(string malId, out AniListNegativeState state)
    {
        if (cache.TryGetValue(NegativeKey(malId), out var raw) && raw is AniListNegativeState known)
        {
            ClearLoading(malId);
            state = known;
            return true;
        }

        state = default;
        return false;
    }

    /// <summary>Remembers a negative answer for 5 minutes.</summary>
    public void Set(string malId, AniListNegativeState state) =>
        cache.Set(NegativeKey(malId), state, NegativeTtl);

    /// <summary>Forgets any negative answer (e.g. after rows were stored or found).</summary>
    public void Clear(string malId) => cache.Remove(NegativeKey(malId));

    public void MarkLoading(string malId) => cache.Set(LoadingKey(malId), true, LoadingTtl);

    public void ClearLoading(string malId) => cache.Remove(LoadingKey(malId));

    public bool IsLoading(string malId) => cache.TryGetValue(LoadingKey(malId), out _);
}
