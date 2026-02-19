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
                
                // Refresh cache for all sources (Plex, MAL, Sonarr)
                var deletePlexCacheTask = cacheEntryService.BulkDeleteBySourceAsync(AnimeSource.Plex);
                var deleteMalCacheTask = cacheEntryService.BulkDeleteBySourceAsync(AnimeSource.MyAnimeList);
                var deleteSonarrCacheTask = cacheEntryService.BulkDeleteBySourceAsync(AnimeSource.Sonarr);
                
                await Task.WhenAll(deletePlexCacheTask, deleteMalCacheTask, deleteSonarrCacheTask);
                
                var deletePlexResult = await deletePlexCacheTask;
                if (deletePlexResult.IsFailure)
                    return Result.Failure<IEnumerable<Anime>>(deletePlexResult.Error);
                    
                var deleteMalResult = await deleteMalCacheTask;
                if (deleteMalResult.IsFailure)
                    return Result.Failure<IEnumerable<Anime>>(deleteMalResult.Error);
                    
                var deleteSonarrResult = await deleteSonarrCacheTask;
                if (deleteSonarrResult.IsFailure)
                    return Result.Failure<IEnumerable<Anime>>(deleteSonarrResult.Error);
                
                var addPlexCacheTask = cacheEntryService.BulkInsertAsync(plexResult.Value);
                var addMalCacheTask = cacheEntryService.BulkInsertAsync(malResult.Value);
                var addSonarrCacheTask = cacheEntryService.BulkInsertAsync(sonarrResult.Value);
                
                await Task.WhenAll(addPlexCacheTask, addMalCacheTask, addSonarrCacheTask);
                
                var addPlexResult = await addPlexCacheTask;
                if (addPlexResult.IsFailure)
                    return Result.Failure<IEnumerable<Anime>>(addPlexResult.Error);
                    
                var addMalResult = await addMalCacheTask;
                if (addMalResult.IsFailure)
                    return Result.Failure<IEnumerable<Anime>>(addMalResult.Error);
                    
                var addSonarrResult = await addSonarrCacheTask;
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
