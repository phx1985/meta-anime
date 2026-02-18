using MetaAnime.Application.DTOs;
using MetaAnime.Application.Interfaces;
using MetaAnime.Core.Enums;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Interfaces.Shared.Results;
using MetaAnime.Core.Models;
using MetaAnime.Core.Settings;
using MetaAnime.Infrastructure.MyAnimeList.Models;
using MetaAnime.Infrastructure.Plex.Models;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace MetaAnime.Application.Services
{
    public class AnimeMediatorService(
        IAnimeMappingService animeMappingService,
        IPlexAnimeSourceService<PlexMedia> plexAnimeSource,
        IAnimeMatchingService animeMatchingService,
        IAnimeService animeService,
        IOptions<MalSettings> malOptions,
        IOptions<SonarrSettings> sonarrOptions,
        IOptions<PlexSettings> plexOptions)
        : IAnimeMediatorService
    {
        private readonly string myAnimeListHost = malOptions.Value.WebBaseUrl;
        private readonly string sonarrHost = sonarrOptions.Value.Host;
        // TODO : Add sonarr web url to the configuration

        private readonly string plexBaseUrl = $"{plexOptions.Value.Host}/desktop/#!/server/{plexAnimeSource.GetMachineIdentifierAsync().Result.Value}/details?key=%2Flibrary%2Fmetadata%2F";
        // TODO : Add plex web url to the configuration
        public async Task<Result<PaginatedResult<GetAnimeDto>>> GetAllAsync(int page = 1, int pageSize = 100)
        {
            try
            {
                // Validate pagination parameters
                if (page < 1) page = 1;
                if (pageSize < 1) pageSize = 1;
                if (pageSize > 100) pageSize = 100;

                var mappingsResult = await animeMappingService.GetAllAsync(includeAnime: true);
                if (mappingsResult.IsFailure)
                {
                    return Result.Failure<PaginatedResult<GetAnimeDto>>(mappingsResult.Error);
                }

                var groupedMappings = mappingsResult.Value
                    .OrderBy(m => m.AniDbId)
                    .ThenBy(m => m.Anime?.AnimeSource)
                    .GroupBy(m => m.AniDbId);

                var allAnimeDtos = groupedMappings.AsParallel().Select(g => new GetAnimeDto(
                    g.Key,
                    g.FirstOrDefault()?.Anime?.Title ?? "NO_TITLE",
                    [.. g.Where(m => m.Anime?.AnimeSource == AnimeSource.Plex).Select(m => new PlexAnimeDto(m.Anime, plexBaseUrl))],
                    [.. g.Where(m => m.Anime?.AnimeSource == AnimeSource.MyAnimeList).Select(m => new MalAnimeDto(m.Anime, myAnimeListHost))],
                    [.. g.Where(m => m.Anime?.AnimeSource == AnimeSource.Sonarr).Select(m => new SonarrAnimeDto(m.Anime, sonarrHost))]
                ))
                .OrderBy(a => a.Title)
                .ToList();

                var totalCount = allAnimeDtos.Count;
                var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

                var paginatedItems = allAnimeDtos
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                var paginatedResult = new PaginatedResult<GetAnimeDto>(
                    paginatedItems,
                    page,
                    pageSize,
                    totalCount,
                    totalPages
                );

                return Result.Success(paginatedResult);
            }
            catch (Exception ex)
            {
                return Result.Failure<PaginatedResult<GetAnimeDto>>(new("Error.GetAllAsync", ex.Message));
            }
        }

        public async Task<Result> SyncAllAsync(bool forceRefreshKometaMapping = false)
        {
            try
            {
                var matchResult = await animeMatchingService.MatchAsync(forceRefreshKometaMapping);
                if (matchResult.IsFailure)
                {
                    return Result.Failure(matchResult.Error);
                }
                return Result.Success();
            }
            catch (Exception ex)
            {
                return Result.Failure(new("Error.SyncAllAsync", ex.Message));
            }
        }

        public async Task<Result<GetAnimeDto?>> GetByIdAsync(string aniDbId)
        {
            try
            {
                var mappingsResult = await animeMappingService.GetAllAsync(includeAnime: true);
                if (mappingsResult.IsFailure)
                {
                    return Result.Failure<GetAnimeDto?>(mappingsResult.Error);
                }

                var animeMappings = mappingsResult.Value
                    .Where(m => m.AniDbId == aniDbId)
                    .OrderBy(m => m.Anime?.AnimeSource)
                    .ToList();

                if (!animeMappings.Any())
                {
                    return Result.Success<GetAnimeDto?>(null);
                }

                var animeDto = new GetAnimeDto(
                    aniDbId,
                    animeMappings.FirstOrDefault()?.Anime?.Title ?? "NO_TITLE",
                    [.. animeMappings.Where(m => m.Anime?.AnimeSource == AnimeSource.Plex).Select(m => new PlexAnimeDto(m.Anime, plexBaseUrl))],
                    [.. animeMappings.Where(m => m.Anime?.AnimeSource == AnimeSource.MyAnimeList).Select(m => new MalAnimeDto(m.Anime, myAnimeListHost))],
                    [.. animeMappings.Where(m => m.Anime?.AnimeSource == AnimeSource.Sonarr).Select(m => new SonarrAnimeDto(m.Anime, sonarrHost))]
                );

                return Result.Success<GetAnimeDto?>(animeDto);
            }
            catch (Exception ex)
            {
                return Result.Failure<GetAnimeDto?>(new("Error.GetByIdAsync", ex.Message));
            }
        }

        public async Task<Result<AnimeDto>> CreateAsync(CreateAnimeRequest request)
        {
            try
            {
                var anime = new Anime
                {
                    SourceId = request.SourceId,
                    Title = request.Title,
                    Description = request.Description,
                    AnimeSource = request.AnimeSource
                };

                var insertResult = await animeService.BulkInsertAsync([anime]);
                if (insertResult.IsFailure)
                {
                    return Result.Failure<AnimeDto>(insertResult.Error);
                }

                return Result.Success(AnimeDto.FromAnime(anime));
            }
            catch (Exception ex)
            {
                return Result.Failure<AnimeDto>(new("Error.CreateAsync", ex.Message));
            }
        }

        public async Task<Result<AnimeDto>> UpdateAsync(Guid id, UpdateAnimeRequest request)
        {
            try
            {
                var animeResult = await animeService.GetByIdAsync(id);
                if (animeResult.IsFailure)
                {
                    return Result.Failure<AnimeDto>(animeResult.Error);
                }

                var anime = animeResult.Value;
                if (anime == null)
                {
                    return Result.Failure<AnimeDto>(new("Error.NotFound", "Anime not found"));
                }

                anime.Title = request.Title;
                anime.Description = request.Description;

                var updateResult = await animeService.BulkInsertAsync([anime]);
                if (updateResult.IsFailure)
                {
                    return Result.Failure<AnimeDto>(updateResult.Error);
                }

                return Result.Success(AnimeDto.FromAnime(anime));
            }
            catch (Exception ex)
            {
                return Result.Failure<AnimeDto>(new("Error.UpdateAsync", ex.Message));
            }
        }

        public async Task<Result> DeleteAsync(Guid id)
        {
            try
            {
                var animeResult = await animeService.GetByIdAsync(id);
                if (animeResult.IsFailure)
                {
                    return Result.Failure(animeResult.Error);
                }

                if (animeResult.Value == null)
                {
                    return Result.Failure(new("Error.NotFound", "Anime not found"));
                }

                var deleteResult = await animeService.DeleteAsync(id);
                if (deleteResult.IsFailure)
                {
                    return Result.Failure(deleteResult.Error);
                }

                return Result.Success();
            }
            catch (Exception ex)
            {
                return Result.Failure(new("Error.DeleteAsync", ex.Message));
            }
        }

    }
}
