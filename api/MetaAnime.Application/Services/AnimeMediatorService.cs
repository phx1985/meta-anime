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
        public async Task<Result<IEnumerable<GetAnimeDto>>> GetAllAsync()
        {
            try
            {
                var mappingsResult = await animeMappingService.GetAllAsync(includeAnime: true);
                if (mappingsResult.IsFailure)
                {
                    return Result.Failure<IEnumerable<GetAnimeDto>>(mappingsResult.Error);
                }

                var groupedMappings = mappingsResult.Value
                    .OrderBy(m => m.AniDbId)
                    .ThenBy(m => m.Anime?.AnimeSource)
                    .GroupBy(m => m.AniDbId);

                var animeDtos = groupedMappings.AsParallel().Select(g => new GetAnimeDto
                {
                    Title = g.FirstOrDefault()?.Anime?.Title ?? "NO_TITLE",
                    AniDbId = g.Key,
                    MalAnimes = [.. g.Where(m => m.Anime?.AnimeSource == AnimeSource.MyAnimeList).Select(m => new MalAnimeDto(m.Anime, myAnimeListHost))],
                    PlexAnimes = [.. g.Where(m => m.Anime?.AnimeSource == AnimeSource.Plex).Select(m => new PlexAnimeDto(m.Anime, plexBaseUrl))],
                    SonarrAnimes = [.. g.Where(m => m.Anime?.AnimeSource == AnimeSource.Sonarr).Select(m => new SonarrAnimeDto(m.Anime, sonarrHost))]
                }).OrderBy(a => a.Title).ToList();

                return Result.Success<IEnumerable<GetAnimeDto>>(animeDtos);
            }
            catch (Exception ex)
            {
                return Result.Failure<IEnumerable<GetAnimeDto>>(new("Error.GetAllAsync", ex.Message));
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

    }
}
