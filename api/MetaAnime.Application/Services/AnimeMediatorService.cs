using MetaAnime.Application.Contracts;
using MetaAnime.Application.Interfaces;
using MetaAnime.Domain.Enums;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Interfaces.Shared.Results;
using MetaAnime.Domain.Entities;
using MetaAnime.Core.Settings;
using MetaAnime.Infrastructure.MyAnimeList.Models;
using MetaAnime.Infrastructure.Plex.Models;
using MetaAnime.Infrastructure.SonarrAPIs.Sonarr.Models;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace MetaAnime.Application.Services
{
    public class AnimeMediatorService(
        IAnimeMappingService animeMappingService,
        IPlexAnimeSourceService<PlexMedia> plexAnimeSource,
        IMalAnimeSourceService<MalAnime> malAnimeSource,
        ISonarrAnimeSourceService<SonarrSeries> sonarrAnimeSource,
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

                var animeDtos = new List<AnimeDetailsResponse>();

                foreach (var group in groupedMappings)
                {
                    var plexAnimes = new List<PlexAnimeInfo>();
                    var malAnimes = new List<MalAnimeInfo>();
                    var sonarrAnimes = new List<SonarrAnimeInfo>();

                    // Fetch Plex anime details
                    foreach (var mapping in group.Where(m => m.Anime?.AnimeSource == AnimeSource.Plex))
                    {
                        var plexResult = await plexAnimeSource.GetByIdAsync(mapping.Anime!.SourceId);
                        if (plexResult.IsSuccess && plexResult.Value != null)
                        {
                            plexAnimes.Add(new PlexAnimeInfo(
                                plexResult.Value,
                                plexBaseUrl,
                                plexResult.Value.ViewedLeafCount,
                                plexResult.Value.LeafCount
                            ));
                        }
                        else
                        {
                            plexAnimes.Add(new PlexAnimeInfo(mapping.Anime, plexBaseUrl));
                        }
                    }

                    // Fetch MAL anime details
                    foreach (var mapping in group.Where(m => m.Anime?.AnimeSource == AnimeSource.MyAnimeList))
                    {
                        var malResult = await malAnimeSource.GetByIdAsync(mapping.Anime!.SourceId);
                        if (malResult.IsSuccess && malResult.Value != null)
                        {
                            malAnimes.Add(new MalAnimeInfo(malResult.Value, myAnimeListHost));
                        }
                        else
                        {
                            malAnimes.Add(new MalAnimeInfo(mapping.Anime, myAnimeListHost));
                        }
                    }

                    // Fetch Sonarr anime details
                    foreach (var mapping in group.Where(m => m.Anime?.AnimeSource == AnimeSource.Sonarr))
                    {
                        var sonarrResult = await sonarrAnimeSource.GetByIdAsync(mapping.Anime!.SourceId);
                        if (sonarrResult.IsSuccess && sonarrResult.Value != null)
                        {
                            var codecDistribution = await sonarrAnimeSource.GetCodecDistributionAsync(mapping.Anime!.SourceId);
                            sonarrAnimes.Add(new SonarrAnimeInfo(sonarrResult.Value, sonarrHost, codecDistribution));
                        }
                        else
                        {
                            sonarrAnimes.Add(new SonarrAnimeInfo(mapping.Anime, sonarrHost));
                        }
                    }

                    animeDtos.Add(new AnimeDetailsResponse(
                        group.Key,
                        group.FirstOrDefault()?.Anime?.Title ?? "NO_TITLE",
                        [.. plexAnimes],
                        [.. malAnimes],
                        [.. sonarrAnimes]
                    ));
                }

                return Result.Success<IEnumerable<AnimeDetailsResponse>>(animeDtos.OrderBy(a => a.Title));
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

                var plexAnimes = new List<PlexAnimeInfo>();
                var malAnimes = new List<MalAnimeInfo>();
                var sonarrAnimes = new List<SonarrAnimeInfo>();

                // Fetch Plex anime details
                foreach (var mapping in animeMappings.Where(m => m.Anime?.AnimeSource == AnimeSource.Plex))
                {
                    var plexResult = await plexAnimeSource.GetByIdAsync(mapping.Anime!.SourceId);
                    if (plexResult.IsSuccess && plexResult.Value != null)
                    {
                        plexAnimes.Add(new PlexAnimeInfo(
                            plexResult.Value,
                            plexBaseUrl,
                            plexResult.Value.ViewedLeafCount,
                            plexResult.Value.LeafCount
                        ));
                    }
                    else
                    {
                        plexAnimes.Add(new PlexAnimeInfo(mapping.Anime, plexBaseUrl));
                    }
                }

                // Fetch MAL anime details
                foreach (var mapping in animeMappings.Where(m => m.Anime?.AnimeSource == AnimeSource.MyAnimeList))
                {
                    var malResult = await malAnimeSource.GetByIdAsync(mapping.Anime!.SourceId);
                    if (malResult.IsSuccess && malResult.Value != null)
                    {
                        malAnimes.Add(new MalAnimeInfo(malResult.Value, myAnimeListHost));
                    }
                    else
                    {
                        malAnimes.Add(new MalAnimeInfo(mapping.Anime, myAnimeListHost));
                    }
                }

                // Fetch Sonarr anime details
                foreach (var mapping in animeMappings.Where(m => m.Anime?.AnimeSource == AnimeSource.Sonarr))
                {
                    var sonarrResult = await sonarrAnimeSource.GetByIdAsync(mapping.Anime!.SourceId);
                    if (sonarrResult.IsSuccess && sonarrResult.Value != null)
                    {
                        var codecDistribution = await sonarrAnimeSource.GetCodecDistributionAsync(mapping.Anime!.SourceId);
                        sonarrAnimes.Add(new SonarrAnimeInfo(sonarrResult.Value, sonarrHost, codecDistribution));
                    }
                    else
                    {
                        sonarrAnimes.Add(new SonarrAnimeInfo(mapping.Anime, sonarrHost));
                    }
                }

                var animeDto = new AnimeDetailsResponse(
                    aniDbId,
                    animeMappings.FirstOrDefault()?.Anime?.Title ?? "NO_TITLE",
                    [.. plexAnimes],
                    [.. malAnimes],
                    [.. sonarrAnimes]
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
