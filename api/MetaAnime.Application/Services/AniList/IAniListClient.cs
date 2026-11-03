namespace MetaAnime.Application.Services.AniList;

/// <summary>Transport-level outcome of one logical AniList GraphQL fetch (after retries).</summary>
public enum AniListApiStatus
{
    /// <summary>2xx with a deserialized body (the <c>data</c> payload may still contain nulls).</summary>
    Success,
    /// <summary>AniList says the resource does not exist — a definitive answer.</summary>
    NotFound,
    /// <summary>AniList is unreachable, rate-limited, or errored on every attempt.</summary>
    Unavailable,
}

public sealed record AniListApiResult<T>(AniListApiStatus Status, T? Data, string? Detail = null)
{
    public static AniListApiResult<T> Ok(T? data, string? detail = null) => new(AniListApiStatus.Success, data, detail);
    public static AniListApiResult<T> Missing(string? detail = null) => new(AniListApiStatus.NotFound, default, detail);
    public static AniListApiResult<T> Down(string? detail = null) => new(AniListApiStatus.Unavailable, default, detail);
}

/// <summary>Resilient GraphQL POSTs against the AniList GraphQL API.</summary>
public interface IAniListClient
{
    /// <summary>
    /// POSTs <paramref name="query"/> with <paramref name="variables"/> and deserializes the
    /// response's <c>data</c> payload into <typeparamref name="T"/>. Never throws: failures
    /// surface as <see cref="AniListApiStatus.Unavailable"/>. A null payload with 404 GraphQL
    /// errors surfaces as <see cref="AniListApiStatus.NotFound"/>.
    /// </summary>
    Task<AniListApiResult<T>> PostQueryAsync<T>(
        string query,
        IReadOnlyDictionary<string, object?>? variables,
        CancellationToken cancellationToken = default);
}
