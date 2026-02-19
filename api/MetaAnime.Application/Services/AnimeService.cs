using MetaAnime.Domain.Enums;
using MetaAnime.Core.Interfaces.Mappers;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Interfaces.Shared.Results;
using MetaAnime.Domain.Entities;
using MetaAnime.Infrastructure.MyAnimeList.Models;
using MetaAnime.Infrastructure.Plex.Models;
using MetaAnime.Infrastructure.SonarrAPIs.Sonarr.Models;

namespace MetaAnime.Application.Services
{
    public class AnimeService(
        IAnimeRepository animeRepository,
        IPlexAnimeSourceService<PlexMedia> plexAnimeSourceService,
        IMalAnimeSourceService<MalAnime> malAnimeSourceService,
        ISonarrAnimeSourceService<SonarrSeries> sonarrAnimeSourceService,
        ICacheEntryService cacheEntryService
        ) : IAnimeService
    {
        public async Task<Result> BulkInsertAsync(IEnumerable<Anime> animes)
        {
            try
            {
                await animeRepository.BulkInsertAsync(animes);
                return Result.Success();
            }
            catch (Exception ex)
            {
                return Result.Failure(new("Error.BulkInsertAsync", ex.Message));
            }
        }

        public async Task<Result> BulkInsertAsync(IEnumerable<IAnimeConvertible> animeConvertibles)
        {
            try
            {
                await animeRepository.BulkInsertAsync(animeConvertibles);
                return Result.Success();
            }
            catch (Exception ex)
            {
                return Result.Failure(new("Error.BulkInsertAsync", ex.Message));
            }
        }

        public async Task<Result> BulkDeleteAllAsync()
        {
            try
            {
                await animeRepository.BulkDeleteAllAsync();
                return Result.Success();
            }
            catch (Exception ex)
            {
                return Result.Failure(new("Error.BulkDeleteAllAsync", ex.Message));
            }
        }

        public async Task<Result> BulkDeleteBySourceAsync(AnimeSource animeSource)
        {
            try
            {
                await animeRepository.BulkDeleteBySourceAsync(animeSource);
                return Result.Success();
            }
            catch (Exception ex)
            {
                return Result.Failure(new("Error.BulkDeleteBySourceAsync", ex.Message));
            }
        }

        public async Task<Result<IEnumerable<Anime>>> GetAllAsync()
        {
            try
            {
                var animes = await animeRepository.GetAllAsync();
                return Result.Success(animes);
            }
            catch (Exception ex)
            {
                return Result.Failure<IEnumerable<Anime>>(new("Error.GetAllAsync", ex.Message));
            }
        }

        public async Task<Result<IEnumerable<Anime>>> GetAllBySourceAsync(AnimeSource source)
        {
            try
            {
                var animes = await animeRepository.GetAllBySourceAsync(source);
                return Result.Success(animes);
            }
            catch (Exception ex)
            {
                return Result.Failure<IEnumerable<Anime>>(new("Error.GetAllBySourceAsync", ex.Message));
            }
        }

        public async Task<Result<Anime?>> GetAnimeAsync(AnimeSource source, string sourceId)
        {
            try
            {
                var anime = await animeRepository.GetAnimeAsync(source, sourceId);
                return Result.Success(anime);
            }
            catch (Exception ex)
            {
                return Result.Failure<Anime?>(new("Error.GetAnimeAsync", ex.Message));
            }
        }

        public async Task<Result<IEnumerable<Anime>>> RefreshData()
        {
            try
            {
                var plexTask = plexAnimeSourceService.GetAllAsync();
                var malTask = malAnimeSourceService.GetAllAsync();
                var sonarrTask = sonarrAnimeSourceService.GetAllAsync();

                // Run the tasks in parallel and wait for them to finish
                await Task.WhenAll(plexTask, malTask, sonarrTask);
                
                // Check if any of the tasks failed
                var plexResult = await plexTask;
                if (plexResult.IsFailure)
                    return Result.Failure<IEnumerable<Anime>>(plexResult.Error);
                    
                var malResult = await malTask;
                if (malResult.IsFailure)
                    return Result.Failure<IEnumerable<Anime>>(malResult.Error);
                    
                var sonarrResult = await sonarrTask;
                if (sonarrResult.IsFailure)
                    return Result.Failure<IEnumerable<Anime>>(sonarrResult.Error);
                    
                // Delete all animes and add the fresh ones
                await animeRepository.BulkDeleteAllAsync();
                await animeRepository.BulkInsertAsync([.. plexResult.Value, .. malResult.Value, .. sonarrResult.Value]);
                
                // Enrich data with detailed information by calling GetByIdAsync for each anime
                // This ensures cache contains complete data (my_list_status for MAL, seasons for Sonarr)
                var enrichedPlexTask = EnrichPlexDataAsync(plexResult.Value);
                var enrichedMalTask = EnrichMalDataAsync(malResult.Value);
                var enrichedSonarrTask = EnrichSonarrDataAsync(sonarrResult.Value);
                
                await Task.WhenAll(enrichedPlexTask, enrichedMalTask, enrichedSonarrTask);
                
                var enrichedPlexData = enrichedPlexTask.Result;
                var enrichedMalData = enrichedMalTask.Result;
                var enrichedSonarrData = enrichedSonarrTask.Result;
                
                // Refresh cache for all sources (Plex, MAL, Sonarr)
                // Run cache operations sequentially to avoid exhausting database connection pool
                var deletePlexResult = await cacheEntryService.BulkDeleteBySourceAsync(AnimeSource.Plex);
                if (deletePlexResult.IsFailure)
                    return Result.Failure<IEnumerable<Anime>>(deletePlexResult.Error);
                    
                var deleteMalResult = await cacheEntryService.BulkDeleteBySourceAsync(AnimeSource.MyAnimeList);
                if (deleteMalResult.IsFailure)
                    return Result.Failure<IEnumerable<Anime>>(deleteMalResult.Error);
                    
                var deleteSonarrResult = await cacheEntryService.BulkDeleteBySourceAsync(AnimeSource.Sonarr);
                if (deleteSonarrResult.IsFailure)
                    return Result.Failure<IEnumerable<Anime>>(deleteSonarrResult.Error);
                
                // Cache the enriched data instead of the basic GetAllAsync results
                var addPlexResult = await cacheEntryService.BulkInsertAsync(enrichedPlexData);
                if (addPlexResult.IsFailure)
                    return Result.Failure<IEnumerable<Anime>>(addPlexResult.Error);
                    
                var addMalResult = await cacheEntryService.BulkInsertAsync(enrichedMalData);
                if (addMalResult.IsFailure)
                    return Result.Failure<IEnumerable<Anime>>(addMalResult.Error);
                    
                var addSonarrResult = await cacheEntryService.BulkInsertAsync(enrichedSonarrData);
                if (addSonarrResult.IsFailure)
                    return Result.Failure<IEnumerable<Anime>>(addSonarrResult.Error);
                    
                // Return the fresh animes
                var freshAnimes = await animeRepository.GetAllAsync();
                return Result.Success(freshAnimes);
            }
            catch (Exception ex)
            {
                return Result.Failure<IEnumerable<Anime>>(new("Error.RefreshData", ex.Message));
            }
        }

        /// <summary>
        /// Enrich Plex anime data by fetching detailed information.
        /// Plex GetAllAsync already returns complete data, so just return as-is.
        /// </summary>
        private Task<IList<PlexMedia>> EnrichPlexDataAsync(IList<PlexMedia> plexAnimes)
        {
            // Plex GetAllAsync already returns complete data with all fields
            return Task.FromResult(plexAnimes);
        }

        /// <summary>
        /// Enrich MAL anime data by calling GetByIdAsync for each anime to get complete my_list_status.
        /// Processes in batches with delay to avoid rate limiting.
        /// Always calls GetByIdAsync to ensure complete data regardless of GetAllAsync results.
        /// </summary>
        private async Task<IList<MalAnime>> EnrichMalDataAsync(IList<MalAnime> malAnimes)
        {
            const int batchSize = 50; // Process 50 anime at a time
            const int delayMs = 100; // 100ms delay between batches
            
            var enrichedAnimes = new List<MalAnime>();
            
            for (int i = 0; i < malAnimes.Count; i += batchSize)
            {
                var batch = malAnimes.Skip(i).Take(batchSize).ToList();
                var tasks = batch.Select(async anime =>
                {
                    // Always fetch detailed data to ensure complete my_list_status
                    var detailedResult = await malAnimeSourceService.GetByIdAsync(anime.Id.ToString());
                    return detailedResult.IsSuccess && detailedResult.Value != null ? detailedResult.Value : anime;
                }).ToList();
                
                var batchResults = await Task.WhenAll(tasks);
                enrichedAnimes.AddRange(batchResults);
                
                // Delay between batches to avoid rate limiting
                if (i + batchSize < malAnimes.Count)
                {
                    await Task.Delay(delayMs);
                }
            }
            
            return enrichedAnimes;
        }

        /// <summary>
        /// Enrich Sonarr anime data by calling GetByIdAsync for each anime to get complete season information.
        /// Processes in batches with delay to avoid overwhelming Sonarr API.
        /// Always calls GetByIdAsync to ensure complete data regardless of GetAllAsync results.
        /// </summary>
        private async Task<IList<SonarrSeries>> EnrichSonarrDataAsync(IList<SonarrSeries> sonarrAnimes)
        {
            const int batchSize = 50; // Process 50 anime at a time
            const int delayMs = 100; // 100ms delay between batches
            
            var enrichedAnimes = new List<SonarrSeries>();
            
            for (int i = 0; i < sonarrAnimes.Count; i += batchSize)
            {
                var batch = sonarrAnimes.Skip(i).Take(batchSize).ToList();
                var tasks = batch.Select(async anime =>
                {
                    // Always fetch detailed data to ensure complete season information
                    var detailedResult = await sonarrAnimeSourceService.GetByIdAsync(anime.Id.ToString());
                    return detailedResult.IsSuccess && detailedResult.Value != null ? detailedResult.Value : anime;
                }).ToList();
                
                var batchResults = await Task.WhenAll(tasks);
                enrichedAnimes.AddRange(batchResults);
                
                // Delay between batches to avoid overwhelming Sonarr
                if (i + batchSize < sonarrAnimes.Count)
                {
                    await Task.Delay(delayMs);
                }
            }
            
            return enrichedAnimes;
        }

        public async Task<Result<Anime?>> GetByIdAsync(Guid id)
        {
            try
            {
                var anime = await animeRepository.GetByIdAsync(id);
                return Result.Success(anime);
            }
            catch (Exception ex)
            {
                return Result.Failure<Anime?>(new("Error.GetByIdAsync", ex.Message));
            }
        }

        public async Task<Result> DeleteAsync(Guid id)
        {
            try
            {
                await animeRepository.DeleteAsync(id);
                return Result.Success();
            }
            catch (Exception ex)
            {
                return Result.Failure(new("Error.DeleteAsync", ex.Message));
            }
        }
    }
}
