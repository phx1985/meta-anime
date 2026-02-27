using Hangfire;
using MetaAnime.Core.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace MetaAnime.Application.BackgroundJobs;

public sealed class MalTokenRefreshJob(
    IMalTokenService malTokenService,
    ILogger<MalTokenRefreshJob> logger)
{
    /// <summary>
    /// Refreshes the MyAnimeList access token using the stored refresh token and persists the new tokens.
    /// </summary>
    public async Task RefreshToken()
    {
        logger.LogInformation(Constants.LogMessages.MalTokenRefreshJob.Starting, DateTime.UtcNow);

        try
        {
            var result = await malTokenService.RefreshTokenAsync();

            if (result.IsSuccess)
            {
                logger.LogInformation(Constants.LogMessages.MalTokenRefreshJob.Succeeded, DateTime.UtcNow);
            }
            else
            {
                logger.LogError(Constants.LogMessages.MalTokenRefreshJob.Failed, result.Error.Message);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, Constants.LogMessages.MalTokenRefreshJob.Error);
            throw; // re-throw to mark the Hangfire job as failed
        }
    }

    /// <summary>
    /// Schedules the MAL token refresh job to run daily.
    /// </summary>
    public static void Schedule(IRecurringJobManager recurringJobManager)
    {
        recurringJobManager.AddOrUpdate<MalTokenRefreshJob>(
            Constants.JobNames.MalTokenRefresh,
            job => job.RefreshToken(),
            Cron.Daily);
        recurringJobManager.Trigger(Constants.JobNames.MalTokenRefresh); // Trigger immediately on startup
    }
}
