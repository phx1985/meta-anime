using MetaAnime.Domain.Enums;
using MetaAnime.Core.Interfaces.Mappers;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Interfaces.Shared.Results;
using MetaAnime.Domain.Entities;
using MetaAnime.Infrastructure.MyAnimeList.Models;
using MetaAnime.Infrastructure.Plex.Models;
using MetaAnime.Infrastructure.SonarrAPIs.Sonarr.Models;

namespace MetaAnime.Application.Services;

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
            return Result.Failure(new(Constants.Errors.BulkInsertAsync, ex.Message));
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
            return Result.Failure(new(Constants.Errors.BulkInsertAsync, ex.Message));
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
            return Result.Failure(new(Constants.Errors.BulkDeleteAllAsync, ex.Message));
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
            return Result.Failure(new(Constants.Errors.BulkDeleteBySourceAsync, ex.Message));
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
            return Result.Failure<IEnumerable<Anime>>(new(Constants.Errors.GetAllAsync, ex.Message));
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
            return Result.Failure<IEnumerable<Anime>>(new(Constants.Errors.GetAllBySourceAsync, ex.Message));
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
            return Result.Failure<Anime?>(new(Constants.Errors.GetAnimeAsync, ex.Message));
        }
    }

    public async Task<Result<IEnumerable<Anime>>> RefreshData()
    {
        try
        {
            // Run sequentially: Sonarr and MAL source services each access
            // IPropertyBagRepository which uses the scoped DbContext. Running
            // them concurrently (Task.WhenAll) causes "second operation started
            // on this context" errors because EF Core DbContext is not thread-safe.
            var plexResult = await plexAnimeSourceService.GetAllAsync();
            if (plexResult.IsFailure)
                return Result.Failure<IEnumerable<Anime>>(plexResult.Error);

            var malResult = await malAnimeSourceService.GetAllAsync();
            if (malResult.IsFailure)
                return Result.Failure<IEnumerable<Anime>>(malResult.Error);

            var sonarrResult = await sonarrAnimeSourceService.GetAllAsync();
            if (sonarrResult.IsFailure)
                return Result.Failure<IEnumerable<Anime>>(sonarrResult.Error);

            // Enrich each Sonarr series with codec distribution so it is included in the cache.
            // Runs sequentially to respect the shared DbContext constraint.
            foreach (var series in sonarrResult.Value)
            {
                series.CodecDistribution = await sonarrAnimeSourceService.GetCodecDistributionAsync(series.Id.ToString());
            }

            await animeRepository.BulkDeleteAllAsync();
            await animeRepository.BulkInsertAsync([.. plexResult.Value, .. malResult.Value, .. sonarrResult.Value]);

            var deletePlexResult = await cacheEntryService.BulkDeleteBySourceAsync(AnimeSource.Plex);
            if (deletePlexResult.IsFailure)
                return Result.Failure<IEnumerable<Anime>>(deletePlexResult.Error);

            var deleteMalResult = await cacheEntryService.BulkDeleteBySourceAsync(AnimeSource.MyAnimeList);
            if (deleteMalResult.IsFailure)
                return Result.Failure<IEnumerable<Anime>>(deleteMalResult.Error);

            var deleteSonarrResult = await cacheEntryService.BulkDeleteBySourceAsync(AnimeSource.Sonarr);
            if (deleteSonarrResult.IsFailure)
                return Result.Failure<IEnumerable<Anime>>(deleteSonarrResult.Error);

            var addPlexResult = await cacheEntryService.BulkInsertAsync(plexResult.Value);
            if (addPlexResult.IsFailure)
                return Result.Failure<IEnumerable<Anime>>(addPlexResult.Error);

            var addMalResult = await cacheEntryService.BulkInsertAsync(malResult.Value);
            if (addMalResult.IsFailure)
                return Result.Failure<IEnumerable<Anime>>(addMalResult.Error);

            var addSonarrResult = await cacheEntryService.BulkInsertAsync(sonarrResult.Value);
            if (addSonarrResult.IsFailure)
                return Result.Failure<IEnumerable<Anime>>(addSonarrResult.Error);

            var freshAnimes = await animeRepository.GetAllAsync();
            return Result.Success(freshAnimes);
        }
        catch (Exception ex)
        {
            return Result.Failure<IEnumerable<Anime>>(new(Constants.Errors.RefreshData, ex.Message));
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
            return Result.Failure<Anime?>(new(Constants.Errors.GetByIdAsync, ex.Message));
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
            return Result.Failure(new(Constants.Errors.DeleteAsync, ex.Message));
        }
    }
}
