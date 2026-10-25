using MetaAnime.Application.Interfaces;
using MetaAnime.Application.Queries;
using MetaAnime.Core.Interfaces.Repositories;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace MetaAnime.Application.Services.BotKun;

public interface IBotKunTasteProfileService
{
    /// <summary>
    /// Returns a one-line summary of the user's library taste (or empty when unavailable),
    /// e.g. "Top Genres: Action, Comedy, Fantasy; 142 matched titles; 1,234/2,000 episodes watched".
    /// Cached for one hour — the underlying statistics aggregate the whole library.
    /// </summary>
    Task<string> GetSummaryAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Builds the Bot-kun taste-profile header from the existing library statistics
/// (AnimeMediatorService.GetStatisticsAsync) — no extra heavy queries required.
/// </summary>
public sealed class BotKunTasteProfileService(
    IAnimeMediatorService animeMediatorService,
    IMemoryCache cache,
    ILogger<BotKunTasteProfileService> logger) : IBotKunTasteProfileService
{
    private const string CacheKey = "botkun:taste-profile";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

    public async Task<string> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue(CacheKey, out string? cached) && cached is not null)
            return cached;

        try
        {
            var result = await animeMediatorService.GetStatisticsAsync();
            if (result.IsFailure || result.Value is null)
                return string.Empty;

            var stats = result.Value;

            var topGenres = stats.GenreStats
                .OrderByDescending(kv => kv.Value)
                .Take(3)
                .Select(kv => kv.Key)
                .ToList();

            var parts = new List<string>();
            if (topGenres.Count > 0)
                parts.Add($"Top Genres: {string.Join(", ", topGenres)}");
            parts.Add($"{stats.MatchedAnime} matched titles");
            if (stats.ViewingProgress is { MalTotalEpisodes: > 0 } vp)
                parts.Add($"{vp.MalEpisodesWatched}/{vp.MalTotalEpisodes} episodes watched");

            var summary = string.Join("; ", parts);
            cache.Set(CacheKey, summary, CacheTtl);
            return summary;
        }
        catch (Exception ex)
        {
            logger.LogWarning("Bot-kun taste profile build failed: {Error}", ex.Message);
            return string.Empty;
        }
    }
}
