using Hangfire;
using MetaAnime.Core.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace MetaAnime.Application.BackgroundJobs;

public class MalTokenRefreshJob(
    IMalTokenService malTokenService,
    ILogger<MalTokenRefreshJob> logger)
{
    /// <summary>
    /// Refreshes the MyAnimeList access token using the stored refresh token and persists the new tokens.
    /// </summary>
    public async Task RefreshToken()
    {
        logger.LogInformation("Starting MAL token refresh at {Time}", DateTime.UtcNow);

        try
        {
            var result = await malTokenService.RefreshTokenAsync();

            if (result.IsSuccess)
            {
                logger.LogInformation("Successfully refreshed MAL token at {Time}", DateTime.UtcNow);
            }
            else
            {
                logger.LogError("Failed to refresh MAL token: {ErrorMessage}", result.Error.Message);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during MAL token refresh");
            throw; // re-throw to mark the Hangfire job as failed
        }
    }

    /// <summary>
    /// Schedules the MAL token refresh job to run daily.
    /// </summary>
    public static void Schedule()
    {
        RecurringJob.AddOrUpdate<MalTokenRefreshJob>(
            "mal-token-refresh",
            job => job.RefreshToken(),
            Cron.Daily);
    }
}
