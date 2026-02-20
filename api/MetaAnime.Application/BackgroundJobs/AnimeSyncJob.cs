using Hangfire;
using MetaAnime.Core.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace MetaAnime.Application.BackgroundJobs;

public class AnimeSyncJob(
    IAnimeMatchingService animeMatchingService,
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
            throw; // re-throw to mark the Hangfire job as failed
        }
    }

    /// <summary>
    /// Schedules the anime sync job to run periodically
    /// </summary>
    public static void Schedule()
    {
        RecurringJob.AddOrUpdate<AnimeSyncJob>(
            "anime-metadata-sync",
            job => job.SyncAnimeMetadata(),
            Cron.Hourly);
    }
}
