using MetaAnime.Core.Helpers;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Interfaces.Shared.Results;
using MetaAnime.Infrastructure.SonarrAPIs.Sonarr.Models;
using RestSharp;
using System.Net;
using System.Text.Json;

namespace MetaAnime.Infrastructure.Sonarr.Services
{
    public class SonarrAnimeSourceService : ISonarrAnimeSourceService<SonarrSeries>
    {
        #region Fields
        private readonly string sonarrHost;
        private readonly string apiKey;
        private readonly List<string> tags;
        #endregion

        #region Constructor
        public SonarrAnimeSourceService()
        {
            sonarrHost = ConfigurationHelper.GetEnvironmentVariableOrThrow("SONARR_HOST");
            apiKey = ConfigurationHelper.GetEnvironmentVariableOrThrow("SONARR_API_KEY");
            tags = [.. ConfigurationHelper.GetEnvironmentVariableOrThrow("SONARR_ANIME_TAGS").Split(',')];
        }
        #endregion

        #region ISonarrSourceService Implementation
        /// <summary>
        /// Allows to get all anime series from Sonarr
        /// </summary>
        /// <returns></returns>
        public async Task<Result<IList<SonarrSeries>>> GetAllAsync()
        {
            var sonarrTags = await GetSonarrTagsAsync();
            var restClient = new RestClient(sonarrHost);
            var restRequest = new RestRequest("api/v3/series");
            restRequest.AddParameter("apikey", apiKey);
            var response = await restClient.ExecuteAsync(restRequest);

            if (response.IsSuccessful)
            {
                var json = response.Content!;
                var allSeries = JsonSerializer.Deserialize<SonarrSeries[]>(json)!;
                IList<SonarrSeries> animes = [.. allSeries.Where(a => IsAnimeTagged(a, sonarrTags!))];
                return Result.Success(animes);
            }
            else if (response.StatusCode == HttpStatusCode.NotFound)
                return Result.Failure<IList<SonarrSeries>>(new("TODO_NOTFOUND"));
            else
                throw new HttpRequestException($"Failed to get Sonarr series: {response.StatusCode} - {response.Content}");
        }

        public async Task<Result<SonarrSeries>> GetByIdAsync(string id)
        {
            var restClient = new RestClient(sonarrHost);
            var restRequest = new RestRequest($"api/v3/series/{id}");
            restRequest.AddParameter("apikey", apiKey);
            var response = await restClient.ExecuteAsync(restRequest);

            if (response.IsSuccessful)
            {
                var json = response.Content!;
                var anime = JsonSerializer.Deserialize<SonarrSeries>(json)!;
                return Result.Success(anime);
            }
            else if (response.StatusCode == HttpStatusCode.NotFound)
                return Result.Failure<SonarrSeries>(new("TODO_NOTFOUND"));
            else
                throw new HttpRequestException($"Failed to get Sonarr series: {response.StatusCode} - {response.Content}");
        }
        #endregion

        #region Private Methods
        private async Task<List<SonarrTag>?> GetSonarrTagsAsync()
        {
            var restClient = new RestClient(sonarrHost);
            var restRequest = new RestRequest("api/v3/tag");
            restRequest.AddParameter("apikey", apiKey);
            var response = await restClient.ExecuteAsync(restRequest);
            if (response.IsSuccessful)
            {
                var json = response.Content!;
                var sonarrTags = JsonSerializer.Deserialize<SonarrTag[]>(json)!;
                var filteredTags = sonarrTags.Where(st => tags.Any(t => t.Equals(st.Label, StringComparison.InvariantCultureIgnoreCase))).ToList();
                return filteredTags;
            }
            else if (response.StatusCode == HttpStatusCode.NotFound)
                return null;
            else
                throw new HttpRequestException($"Failed to get Sonarr tags: {response.StatusCode} - {response.Content}");
        }

        /// <summary>
        /// Check if the anime is tagged as anime
        /// </summary>
        /// <param name="anime"></param>
        /// <param name="sonarrTags"></param>
        /// <returns></returns>
        private bool IsAnimeTagged(SonarrSeries anime, List<SonarrTag> sonarrTags)
        {
            if (tags.Count == 0)
                return false;
            return anime.Tags.Any(tagId => sonarrTags.Any(sonarrTag => sonarrTag.Id == tagId));
        }
        #endregion
    }
}
