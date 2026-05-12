using Hangfire;
using MetaAnime.Application.Interfaces;
using MetaAnime.Core;
using MetaAnime.Core.Interfaces.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace MetaAnime.Application.BackgroundJobs;

public sealed class DownloadStatsCacheJob(
    IAnimeMediatorService animeMediatorService,
    IPropertyBagRepository propertyBagRepository,
    ILogger<DownloadStatsCacheJob> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Fetches download stats from Sonarr and persists them in the PropertyBag cache.
    /// </summary>
    public async Task CacheDownloadStatsAsync()
    {
        logger.LogInformation(Constants.LogMessages.DownloadStatsCacheJob.Starting, DateTime.UtcNow);

        try
        {
            var result = await animeMediatorService.GetActivityStatsAsync(0);

            if (result.IsFailure)
            {
                logger.LogError(Constants.LogMessages.DownloadStatsCacheJob.Error + ": {ErrorMessage}", result.Error.Message);
                return;
            }

            var json = JsonSerializer.Serialize(result.Value, JsonOptions);
            var lastUpdated = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"); // ISO 8601 UTC — formatted to local time on the client

            await propertyBagRepository.UpsertAsync(SettingsKeys.DownloadStatsCache, json);
            await propertyBagRepository.UpsertAsync(SettingsKeys.DownloadStatsCacheLastUpdated, lastUpdated);

            logger.LogInformation(Constants.LogMessages.DownloadStatsCacheJob.Succeeded, DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, Constants.LogMessages.DownloadStatsCacheJob.Error);
            throw; // re-throw to mark the Hangfire job as failed
        }
    }

    /// <summary>
    /// Schedules (or reschedules) the download stats cache job using the interval configured in the PropertyBag.
    /// Triggers the job immediately so the cache is populated on startup.
    /// </summary>
    public static async Task ScheduleAsync(IRecurringJobManager recurringJobManager, IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IPropertyBagRepository>();
        var entry = await repo.GetByKeyAsync(SettingsKeys.DownloadStatsCacheInterval);
        var cronExpr = MapIntervalToCron(entry?.Value ?? "1h");

        recurringJobManager.AddOrUpdate<DownloadStatsCacheJob>(
            Constants.JobNames.DownloadStatsCache,
            job => job.CacheDownloadStatsAsync(),
            cronExpr);

        recurringJobManager.Trigger(Constants.JobNames.DownloadStatsCache);
    }

    private static string MapIntervalToCron(string interval) => interval switch
    {
        "1h"  => Cron.Hourly(),
        "3h"  => "0 */3 * * *",
        "6h"  => "0 */6 * * *",
        "12h" => "0 */12 * * *",
        _     => Cron.Hourly(), // "1h" is the default
    };
}
