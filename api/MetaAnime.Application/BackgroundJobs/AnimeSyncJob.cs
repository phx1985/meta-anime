using Hangfire;
using MetaAnime.Core;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Interfaces.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MetaAnime.Application.BackgroundJobs;

public sealed class AnimeSyncJob(
    IAnimeMatchingService animeMatchingService,
    ISyncProgressNotifier progressNotifier,
    ILogger<AnimeSyncJob> logger)
{
    /// <summary>
    /// Synchronizes anime metadata from external sources
    /// </summary>
    public async Task SyncAnimeMetadata(bool forceRefreshKometaMapping = false)
    {
        logger.LogInformation(Constants.LogMessages.AnimeSyncJob.Starting, DateTime.UtcNow);

        try
        {
            var matchResult = await animeMatchingService.MatchAsync(forceRefreshKometaMapping, progressNotifier);

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
    /// Schedules (or reschedules) the anime sync job using the interval configured in the PropertyBag.
    /// </summary>
    public static async Task ScheduleAsync(IRecurringJobManager recurringJobManager, IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IPropertyBagRepository>();
        var entry = await repo.GetByKeyAsync(SettingsKeys.AppSyncInterval);
        var cronExpr = MapIntervalToCron(entry?.Value ?? "daily");

        recurringJobManager.AddOrUpdate<AnimeSyncJob>(
            Constants.JobNames.AnimeMetadataSync,
            job => job.SyncAnimeMetadata(true),
            cronExpr);
    }

    private static string MapIntervalToCron(string interval) => interval switch
    {
        "hourly" => Cron.Hourly(),
        "every6h" => "0 */6 * * *",
        "every12h" => "0 */12 * * *",
        "weekly" => Cron.Weekly(),
        _ => Cron.Daily(), // "daily" is the default
    };
}
