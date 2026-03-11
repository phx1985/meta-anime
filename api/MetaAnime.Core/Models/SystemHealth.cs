namespace MetaAnime.Core.Models;

public enum ProviderHealthStatus { Unknown, Online, Degraded, Offline }

/// <summary>Result of a single provider health probe.</summary>
public record ProviderHealthResult(
    ProviderHealthStatus Status,
    long LatencyMs,
    string? Message,
    DateTimeOffset CheckedAt);

/// <summary>Point-in-time snapshot of all provider health states.</summary>
public record SystemHealthSnapshot(
    ProviderHealthResult Plex,
    ProviderHealthResult Sonarr,
    ProviderHealthResult Mal,
    DateTimeOffset GeneratedAt);
