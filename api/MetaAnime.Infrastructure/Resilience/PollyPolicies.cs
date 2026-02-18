using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Extensions.Http;

namespace MetaAnime.Infrastructure.Resilience;

public static class PollyPolicies
{
    /// <summary>
    /// Retry policy for transient HTTP errors with exponential backoff
    /// </summary>
    public static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError() // 408, 5xx errors
            .OrResult(msg => msg.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (outcome, timespan, retryCount, context) =>
                {
                    // Log retry attempts if needed
                    Console.WriteLine($"Retry {retryCount} after {timespan.TotalSeconds}s delay due to: {outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString()}");
                });
    }

    /// <summary>
    /// Circuit breaker policy to prevent cascading failures
    /// </summary>
    public static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: 5,
                durationOfBreak: TimeSpan.FromSeconds(30),
                onBreak: (outcome, timespan) =>
                {
                    Console.WriteLine($"Circuit breaker opened for {timespan.TotalSeconds}s");
                },
                onReset: () =>
                {
                    Console.WriteLine("Circuit breaker reset");
                });
    }

    /// <summary>
    /// Timeout policy to prevent hanging requests
    /// </summary>
    public static IAsyncPolicy<HttpResponseMessage> GetTimeoutPolicy()
    {
        return Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// Combined policy with retry, circuit breaker, and timeout
    /// </summary>
    public static IAsyncPolicy<HttpResponseMessage> GetCombinedPolicy()
    {
        return Policy.WrapAsync(
            GetRetryPolicy(),
            GetCircuitBreakerPolicy(),
            GetTimeoutPolicy()
        );
    }
}
