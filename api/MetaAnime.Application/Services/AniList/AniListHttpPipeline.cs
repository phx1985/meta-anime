using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MetaAnime.Core.Settings;

namespace MetaAnime.Application.Services.AniList;

/// <summary>
/// Resilient POST pipeline for the AniList GraphQL API. Every outbound attempt: queues through
/// the shared <see cref="AniListPacingGate"/>, carries the required headers (User-Agent
/// "SeiyuuNexusClient/3.0", Accept application/json, Content-Type application/json), and is
/// bounded by a hard 10 s per-attempt timeout via a linked CTS (never HttpClient's 100 s
/// default). At most 2 retries (3 attempts total), each with a FRESH
/// <see cref="HttpRequestMessage"/> and its own <see cref="StringContent"/> carrying the same
/// serialized <c>{ "query", "variables" }</c> body. 429/503 honor Retry-After capped at 5 s,
/// else 2 s/4 s backoff; network faults/timeouts back off 1 s/2 s. Never throws.
/// </summary>
public sealed class AniListHttpPipeline(
    IHttpClientFactory httpClientFactory,
    AniListPacingGate pacingGate,
    IOptions<AniListSettings> anilistOptions,
    ILogger<AniListHttpPipeline> logger) : IAniListClient
{
    public const string HttpClientName = "AniList";
    public const string UserAgent = "SeiyuuNexusClient/3.0";

    private const int MaxAttempts = 3; // initial attempt + max 2 retries

    private static readonly TimeSpan PerAttemptTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RetryAfterCap = TimeSpan.FromSeconds(5);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public async Task<AniListApiResult<T>> PostQueryAsync<T>(
        string query,
        IReadOnlyDictionary<string, object?>? variables,
        CancellationToken cancellationToken = default)
    {
        // Serialize once — every retry sends the identical JSON body.
        string body;
        try
        {
            body = JsonSerializer.Serialize(new { query, variables }, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to serialize the AniList GraphQL request body.");
            return AniListApiResult<T>.Down($"Request serialization failed: {ex.Message}");
        }

        string? lastDetail = null;

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            // Every outbound request goes through the pacing queue (>15 s queue wait → drop).
            if (!await pacingGate.TryAcquireAsync(cancellationToken))
                return AniListApiResult<T>.Down("Dropped by the AniList pacing gate (queue wait exceeded budget).");

            var client = httpClientFactory.CreateClient(HttpClientName);
            var configuredUrl = anilistOptions.Value.ApiUrl;
            var endpoint = client.BaseAddress
                ?? new Uri(string.IsNullOrWhiteSpace(configuredUrl) ? AniListSettings.DefaultApiUrl : configuredUrl);

            // FRESH HttpRequestMessage + StringContent per attempt — a request message can only
            // be sent once, and its content is consumed by the send.
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            HttpResponseMessage? response = null;
            try
            {
                using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                attemptCts.CancelAfter(PerAttemptTimeout);

                response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, attemptCts.Token);
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    response.Dispose();
                    return AniListApiResult<T>.Missing("AniList endpoint returned HTTP 404.");
                }

                if (response.IsSuccessStatusCode)
                {
                    await using var stream = await response.Content.ReadAsStreamAsync(attemptCts.Token);
                    var envelope = await JsonSerializer.DeserializeAsync<AniListGraphQlEnvelope<T>>(stream, JsonOptions, attemptCts.Token);
                    response.Dispose();
                    return ClassifyEnvelope(envelope);
                }

                lastDetail = $"AniList returned {(int)response.StatusCode} on attempt {attempt + 1}/{MaxAttempts}.";

                if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
                {
                    var delay = ComputeRetryDelay(response.Headers.RetryAfter, attempt);
                    response.Dispose();
                    response = null;

                    if (attempt < MaxAttempts - 1)
                    {
                        await DelayAsync(delay, cancellationToken);
                        continue;
                    }

                    return AniListApiResult<T>.Down(lastDetail);
                }

                // Any other non-success status is not worth retrying.
                response.Dispose();
                return AniListApiResult<T>.Down(lastDetail);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or OperationCanceledException)
            {
                response?.Dispose();
                lastDetail = $"AniList request failed on attempt {attempt + 1}/{MaxAttempts}: {ex.Message}";
                logger.LogWarning(ex, "AniList fetch attempt {Attempt}/{MaxAttempts} failed", attempt + 1, MaxAttempts);

                if (attempt < MaxAttempts - 1)
                {
                    await DelayAsync(FaultBackoffDelay(attempt), cancellationToken);
                    continue;
                }
            }
            catch (Exception ex)
            {
                // Defensive: this pipeline must never throw (no-throw service layer).
                response?.Dispose();
                logger.LogWarning(ex, "Unexpected AniList fetch failure");
                lastDetail = $"AniList request failed: {ex.Message}";
                break;
            }
        }
        return AniListApiResult<T>.Down(lastDetail);
    }

    /// <summary>
    /// Maps the GraphQL envelope onto the transport result: a present <c>data</c> payload is a
    /// Success even when its inner fields are null (e.g. <c>data.Media == null</c> is a
    /// definitive "unknown id", handled by the caller); a null payload is a NotFound only when
    /// GraphQL reported 404, otherwise Unavailable.
    /// </summary>
    private static AniListApiResult<T> ClassifyEnvelope<T>(AniListGraphQlEnvelope<T>? envelope)
    {
        if (envelope is null)
            return AniListApiResult<T>.Down("Empty AniList response body.");

        var detail = envelope.Errors is { Count: > 0 }
            ? string.Join("; ", envelope.Errors.Select(e => e.Message ?? "unknown GraphQL error"))
            : null;

        if (envelope.Data is not null)
            return AniListApiResult<T>.Ok(envelope.Data, detail);

        if (envelope.Errors is { Count: > 0 })
        {
            return envelope.Errors.Any(e => e.Status == 404
                                            || string.Equals(e.Message, "Not Found.", StringComparison.OrdinalIgnoreCase))
                ? AniListApiResult<T>.Missing(detail)
                : AniListApiResult<T>.Down(detail);
        }

        return AniListApiResult<T>.Ok(default, detail);
    }

    /// <summary>Retry-After (capped at 5 s) when the server sent one, else 2 s/4 s backoff.</summary>
    public static TimeSpan ComputeRetryDelay(RetryConditionHeaderValue? retryAfter, int attempt)
    {
        if (retryAfter is not null)
        {
            var wait = retryAfter.Delta
                       ?? (retryAfter.Date is { } date ? date - DateTimeOffset.UtcNow : TimeSpan.Zero);
            if (wait < TimeSpan.Zero)
                wait = TimeSpan.Zero;
            return wait > RetryAfterCap ? RetryAfterCap : wait;
        }

        return TimeSpan.FromSeconds(2 * (attempt + 1));
    }

    /// <summary>Network fault / per-attempt timeout backoff: 1 s then 2 s.</summary>
    public static TimeSpan FaultBackoffDelay(int attempt) => TimeSpan.FromSeconds(1 + attempt);

    private static async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        if (delay <= TimeSpan.Zero)
            return;

        try
        {
            await Task.Delay(delay, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Caller cancellation surfaces on the next gate/attempt check — never throw here.
        }
    }
}
