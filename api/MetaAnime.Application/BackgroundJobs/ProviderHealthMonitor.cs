using System.Diagnostics;
using System.Net.Http.Headers;
using MetaAnime.Core;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MetaAnime.Application.BackgroundJobs;

/// <summary>
/// Periodically probes Plex, Sonarr, and MAL to build a <see cref="SystemHealthSnapshot"/>.
/// Results are cached in <see cref="IProviderHealthCache"/> and served instantly via the API.
/// </summary>
/// <remarks>
/// Uses <see cref="IServiceScopeFactory"/> so each concurrent probe resolves its own
/// <see cref="IPropertyBagRepository"/> — and therefore its own <c>DbContext</c> — preventing
/// EF Core's "second operation started" concurrency exception.
/// </remarks>
public sealed class ProviderHealthMonitor(
    IServiceScopeFactory scopeFactory,
    IProviderHealthCache healthCache,
    ILogger<ProviderHealthMonitor> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);
    private const int DegradedThresholdMs = 1000;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Run immediately on startup so the endpoint is ready before the first 5-min tick.
        await RunChecksAsync(stoppingToken);

        using var timer = new PeriodicTimer(CheckInterval);
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunChecksAsync(stoppingToken);
        }
    }

    private async Task RunChecksAsync(CancellationToken ct)
    {
        var plexTask = ProbePlexAsync(ct);
        var sonarrTask = ProbeSonarrAsync(ct);
        var malTask = ProbeMalAsync(ct);

        await Task.WhenAll(plexTask, sonarrTask, malTask);

        var snapshot = new SystemHealthSnapshot(
            Plex: await plexTask,
            Sonarr: await sonarrTask,
            Mal: await malTask,
            GeneratedAt: DateTimeOffset.UtcNow);

        healthCache.Update(snapshot);
        logger.LogDebug("Health check: Plex={Plex} Sonarr={Sonarr} MAL={Mal}",
            snapshot.Plex.Status, snapshot.Sonarr.Status, snapshot.Mal.Status);
    }

    // ── Plex: GET {host}/identity ─────────────────────────────────────────────
    private async Task<ProviderHealthResult> ProbePlexAsync(CancellationToken ct)
    {
        string? host, token;
        using (var scope = scopeFactory.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IPropertyBagRepository>();
            host = (await repo.GetByKeyAsync(SettingsKeys.PlexHost))?.Value;
            token = (await repo.GetByKeyAsync(SettingsKeys.PlexToken))?.Value;
        }

        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(token))
            return Unknown();

        var url = $"{host.TrimEnd('/')}/identity";
        return await ProbeAsync(url, req =>
        {
            req.Headers.Add("X-Plex-Token", token);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }, ct);
    }

    // ── Sonarr: GET {host}/api/v3/system/status?apikey={key} ─────────────────
    private async Task<ProviderHealthResult> ProbeSonarrAsync(CancellationToken ct)
    {
        string? host, apiKey;
        using (var scope = scopeFactory.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IPropertyBagRepository>();
            host = (await repo.GetByKeyAsync(SettingsKeys.SonarrHost))?.Value;
            apiKey = (await repo.GetByKeyAsync(SettingsKeys.SonarrApiKey))?.Value;
        }

        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(apiKey))
            return Unknown();

        var url = $"{host.TrimEnd('/')}/api/v3/system/status?apikey={Uri.EscapeDataString(apiKey)}";
        return await ProbeAsync(url, _ => { }, ct);
    }

    // ── MAL: GET {baseUrl}/anime/1?fields=id  +  X-MAL-CLIENT-ID header ──────
    private async Task<ProviderHealthResult> ProbeMalAsync(CancellationToken ct)
    {
        string? baseUrl, clientId;
        using (var scope = scopeFactory.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IPropertyBagRepository>();
            baseUrl = (await repo.GetByKeyAsync(SettingsKeys.MalApiBaseUrl))?.Value;
            clientId = (await repo.GetByKeyAsync(SettingsKeys.MalClientId))?.Value;
        }

        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(clientId))
            return Unknown();

        var url = $"{baseUrl.TrimEnd('/')}/anime/1?fields=id";
        return await ProbeAsync(url, req =>
        {
            req.Headers.Add("X-MAL-CLIENT-ID", clientId);
        }, ct);
    }

    // ── Core HTTP probe ───────────────────────────────────────────────────────
    private async Task<ProviderHealthResult> ProbeAsync(
        string url,
        Action<HttpRequestMessage> configureRequest,
        CancellationToken ct)
    {
        using var httpClient = new HttpClient { Timeout = ProbeTimeout };
        var sw = Stopwatch.StartNew();

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            configureRequest(request);

            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            sw.Stop();

            long latency = sw.ElapsedMilliseconds;

            return response.StatusCode switch
            {
                System.Net.HttpStatusCode.OK when latency <= DegradedThresholdMs
                    => new ProviderHealthResult(ProviderHealthStatus.Online, latency, null, DateTimeOffset.UtcNow),
                System.Net.HttpStatusCode.OK
                    => new ProviderHealthResult(ProviderHealthStatus.Degraded, latency, "High latency", DateTimeOffset.UtcNow),
                System.Net.HttpStatusCode.TooManyRequests
                    => new ProviderHealthResult(ProviderHealthStatus.Degraded, latency, "Rate limited", DateTimeOffset.UtcNow),
                _
                    => new ProviderHealthResult(ProviderHealthStatus.Offline, latency, $"HTTP {(int)response.StatusCode}", DateTimeOffset.UtcNow),
            };
        }
        catch (TaskCanceledException)
        {
            return new ProviderHealthResult(ProviderHealthStatus.Offline, ProbeTimeout.Milliseconds, "Timeout", DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Health probe failed for {Url}", url);
            return new ProviderHealthResult(ProviderHealthStatus.Offline, 0, ex.Message, DateTimeOffset.UtcNow);
        }
    }

    private static ProviderHealthResult Unknown() =>
        new(ProviderHealthStatus.Unknown, 0, null, DateTimeOffset.UtcNow);
}
