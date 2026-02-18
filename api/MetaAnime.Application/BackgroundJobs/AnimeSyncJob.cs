using Hangfire;
using MetaAnime.Core.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace MetaAnime.Application.BackgroundJobs;

public class AnimeSyncJob(
    IAnimeMatchingService animeMatchingService,
    IAnimeService animeService,
    ILogger<AnimeSyncJob> logger)
{
    /// <summary>
    /// Synchronizes anime metadata from external sources
    /// </summary>
    public async Task SyncAnimeMetadata()
    {
        logger.LogInformation("Starting anime metadata sync at {Time}", DateTime.UtcNow);
        
        try
        {
            // Refresh data from external sources (Plex, Sonarr, MAL)
            var refreshResult = await animeService.RefreshData();
            
            if (refreshResult.IsFailure)
            {
                logger.LogError("Failed to refresh anime data: {ErrorMessage}", refreshResult.Error.Message);
                return;
            }

            // Match anime across sources using Kometa mapping
            var matchResult = await animeMatchingService.MatchAsync();
            
            if (matchResult.IsSuccess)
            {
                logger.LogInformation("Successfully synced anime metadata at {Time}", DateTime.UtcNow);
            }
            else
            {
                logger.LogError("Failed to match anime: {ErrorMessage}", matchResult.Error.Message);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during anime sync");
            throw; // Re-throw to mark job as failed in Hangfire
        }
    }

    /// <summary>
    /// Schedules the anime sync job to run periodically
    /// </summary>
    public static void Schedule()
    {
        // Run every hour at minute 0
        RecurringJob.AddOrUpdate<AnimeSyncJob>(
            "anime-metadata-sync",
            job => job.SyncAnimeMetadata(),
            Cron.Hourly);
    }
}
