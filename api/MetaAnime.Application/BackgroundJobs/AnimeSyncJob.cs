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
        logger.LogInformation(Constants.LogMessages.AnimeSyncJob.Starting, DateTime.UtcNow);

        try
        {
            var matchResult = await animeMatchingService.MatchAsync();

            if (matchResult.IsSuccess)
            {
                logger.LogInformation(Constants.LogMessages.AnimeSyncJob.Succeeded, DateTime.UtcNow);
            }
            else
            {
                logger.LogError(Constants.LogMessages.AnimeSyncJob.MatchFailed, matchResult.Error.Message);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, Constants.LogMessages.AnimeSyncJob.Error);
            throw; // re-throw to mark the Hangfire job as failed
        }
    }

    /// <summary>
    /// Schedules the anime sync job to run periodically
    /// </summary>
    public static void Schedule(IRecurringJobManager recurringJobManager)
    {
        recurringJobManager.AddOrUpdate<AnimeSyncJob>(
            Constants.JobNames.AnimeMetadataSync,
            job => job.SyncAnimeMetadata(),
            Cron.Hourly);
    }
}
