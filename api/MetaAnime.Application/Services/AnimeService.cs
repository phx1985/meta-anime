using MetaAnime.Core.Enums;
using MetaAnime.Core.Interfaces.Mappers;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Interfaces.Shared.Results;
using MetaAnime.Core.Models;
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
                if (plexTask.Result.IsFailure)
                    return Result.Failure<IEnumerable<Anime>>(plexTask.Result.Error);
                if (malTask.Result.IsFailure)
                    return Result.Failure<IEnumerable<Anime>>(malTask.Result.Error);
                if (sonarrTask.Result.IsFailure)
                    return Result.Failure<IEnumerable<Anime>>(sonarrTask.Result.Error);
                // Delete all animes and add the fresh ones
                await animeRepository.BulkDeleteAllAsync();
                await animeRepository.BulkInsertAsync([.. plexTask.Result.Value, .. malTask.Result.Value, .. sonarrTask.Result.Value]);
                // Refresh the cache for MyAnimeList animes
                var deleteMalCacheResult = await cacheEntryService.BulkDeleteBySourceAsync(AnimeSource.MyAnimeList);
                if (deleteMalCacheResult.IsFailure)
                    return Result.Failure<IEnumerable<Anime>>(deleteMalCacheResult.Error);
                var addMalCacheResult = await cacheEntryService.BulkInsertAsync(malTask.Result.Value);
                if (addMalCacheResult.IsFailure)
                    return Result.Failure<IEnumerable<Anime>>(addMalCacheResult.Error);
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
