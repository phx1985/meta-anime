using MetaAnime.Application.Contracts;
using MetaAnime.Application.Interfaces;
using MetaAnime.Domain.Enums;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Interfaces.Shared.Results;
using MetaAnime.Domain.Entities;
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
        public async Task<Result<IEnumerable<AnimeDetailsResponse>>> GetAllAsync()
        {
            try
            {
                var mappingsResult = await animeMappingService.GetAllAsync(includeAnime: true);
                if (mappingsResult.IsFailure)
                {
                    return Result.Failure<IEnumerable<AnimeDetailsResponse>>(mappingsResult.Error);
                }

                var groupedMappings = mappingsResult.Value
                    .OrderBy(m => m.AniDbId)
                    .ThenBy(m => m.Anime?.AnimeSource)
                    .GroupBy(m => m.AniDbId);

                var animeDtos = groupedMappings.AsParallel().Select(g => new AnimeDetailsResponse(
                    g.Key,
                    g.FirstOrDefault()?.Anime?.Title ?? "NO_TITLE",
                    [.. g.Where(m => m.Anime?.AnimeSource == AnimeSource.Plex).Select(m => new PlexAnimeInfo(m.Anime, plexBaseUrl))],
                    [.. g.Where(m => m.Anime?.AnimeSource == AnimeSource.MyAnimeList).Select(m => new MalAnimeInfo(m.Anime, myAnimeListHost))],
                    [.. g.Where(m => m.Anime?.AnimeSource == AnimeSource.Sonarr).Select(m => new SonarrAnimeInfo(m.Anime, sonarrHost))]
                )).OrderBy(a => a.Title).ToList();

                return Result.Success<IEnumerable<AnimeDetailsResponse>>(animeDtos);
            }
            catch (Exception ex)
            {
                return Result.Failure<IEnumerable<AnimeDetailsResponse>>(new("Error.GetAllAsync", ex.Message));
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

        public async Task<Result<AnimeDetailsResponse?>> GetByIdAsync(string aniDbId)
        {
            try
            {
                var mappingsResult = await animeMappingService.GetAllAsync(includeAnime: true);
                if (mappingsResult.IsFailure)
                {
                    return Result.Failure<AnimeDetailsResponse?>(mappingsResult.Error);
                }

                var animeMappings = mappingsResult.Value
                    .Where(m => m.AniDbId == aniDbId)
                    .OrderBy(m => m.Anime?.AnimeSource)
                    .ToList();

                if (!animeMappings.Any())
                {
                    return Result.Success<AnimeDetailsResponse?>(null);
                }

                var animeDto = new AnimeDetailsResponse(
                    aniDbId,
                    animeMappings.FirstOrDefault()?.Anime?.Title ?? "NO_TITLE",
                    [.. animeMappings.Where(m => m.Anime?.AnimeSource == AnimeSource.Plex).Select(m => new PlexAnimeInfo(m.Anime, plexBaseUrl))],
                    [.. animeMappings.Where(m => m.Anime?.AnimeSource == AnimeSource.MyAnimeList).Select(m => new MalAnimeInfo(m.Anime, myAnimeListHost))],
                    [.. animeMappings.Where(m => m.Anime?.AnimeSource == AnimeSource.Sonarr).Select(m => new SonarrAnimeInfo(m.Anime, sonarrHost))]
                );

                return Result.Success<AnimeDetailsResponse?>(animeDto);
            }
            catch (Exception ex)
            {
                return Result.Failure<AnimeDetailsResponse?>(new("Error.GetByIdAsync", ex.Message));
            }
        }

        public async Task<Result<AnimeResponse>> CreateAsync(CreateAnimeRequest request)
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
                    return Result.Failure<AnimeResponse>(insertResult.Error);
                }

                return Result.Success(AnimeResponse.FromAnime(anime));
            }
            catch (Exception ex)
            {
                return Result.Failure<AnimeResponse>(new("Error.CreateAsync", ex.Message));
            }
        }

        public async Task<Result<AnimeResponse>> UpdateAsync(Guid id, UpdateAnimeRequest request)
        {
            try
            {
                var animeResult = await animeService.GetByIdAsync(id);
                if (animeResult.IsFailure)
                {
                    return Result.Failure<AnimeResponse>(animeResult.Error);
                }

                var anime = animeResult.Value;
                if (anime == null)
                {
                    return Result.Failure<AnimeResponse>(new("Error.NotFound", "Anime not found"));
                }

                anime.Title = request.Title;
                anime.Description = request.Description;

                var updateResult = await animeService.BulkInsertAsync([anime]);
                if (updateResult.IsFailure)
                {
                    return Result.Failure<AnimeResponse>(updateResult.Error);
                }

                return Result.Success(AnimeResponse.FromAnime(anime));
            }
            catch (Exception ex)
            {
                return Result.Failure<AnimeResponse>(new("Error.UpdateAsync", ex.Message));
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
