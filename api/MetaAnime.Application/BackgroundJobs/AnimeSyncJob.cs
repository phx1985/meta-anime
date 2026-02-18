using Hangfire;
using MetaAnime.Core.Interfaces.Services;

namespace MetaAnime.Application.BackgroundJobs;

public class AnimeSyncJob(IAnimeMatchingService animeMatchingService, IAnimeService animeService)
{
    /// <summary>
    /// Synchronizes anime metadata from external sources
    /// </summary>
    public async Task SyncAnimeMetadata()
    {
        Console.WriteLine($"Starting anime metadata sync at {DateTime.UtcNow}");
        
        try
        {
            // Refresh data from external sources (Plex, Sonarr, MAL)
            var refreshResult = await animeService.RefreshData();
            
            if (refreshResult.IsFailure)
            {
                Console.WriteLine($"Failed to refresh anime data: {refreshResult.Error.Message}");
                return;
            }

            // Match anime across sources using Kometa mapping
            var matchResult = await animeMatchingService.MatchAsync();
            
            if (matchResult.IsSuccess)
            {
                Console.WriteLine($"Successfully synced anime metadata at {DateTime.UtcNow}");
            }
            else
            {
                Console.WriteLine($"Failed to match anime: {matchResult.Error.Message}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during anime sync: {ex.Message}");
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
