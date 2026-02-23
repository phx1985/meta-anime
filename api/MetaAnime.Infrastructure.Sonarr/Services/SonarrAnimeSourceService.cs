using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Interfaces.Shared.Results;
using MetaAnime.Core.Settings;
using MetaAnime.Infrastructure.SonarrAPIs.Sonarr.Models;
using Microsoft.Extensions.Options;
using RestSharp;
using System.Net;
using System.Text.Json;

namespace MetaAnime.Infrastructure.Sonarr.Services
{
    public class SonarrAnimeSourceService(IOptions<SonarrSettings> options) : ISonarrAnimeSourceService<SonarrSeries>
    {
        #region Fields
        private readonly string sonarrHost = options.Value.Host;
        private readonly string apiKey = options.Value.ApiKey;
        private readonly List<string> tags = [.. options.Value.AnimeTags.Split(',')];
        #endregion

        #region ISonarrSourceService Implementation
        /// <summary>
        /// Allows to get all anime series from Sonarr
        /// </summary>
        /// <returns></returns>
        public async Task<Result<IList<SonarrSeries>>> GetAllAsync()
        {
            var restClient = new RestClient(sonarrHost);

            var tagsTask = GetSonarrTagsAsync(restClient);
            var seriesTask = GetSonarrSeriesAsync(restClient);

            await Task.WhenAll(tagsTask, seriesTask);

            var sonarrTags = await tagsTask;
            var seriesResponse = await seriesTask;

            if (seriesResponse.IsSuccessful)
            {
                var json = seriesResponse.Content!;
                var allSeries = JsonSerializer.Deserialize<SonarrSeries[]>(json)!;
                IList<SonarrSeries> animes = [.. allSeries.Where(a => IsAnimeTagged(a, sonarrTags!))];
                return Result.Success(animes);
            }
            else if (seriesResponse.StatusCode == HttpStatusCode.NotFound)
                return Result.Failure<IList<SonarrSeries>>(new("TODO_NOTFOUND"));
            else
                throw new HttpRequestException($"Failed to get Sonarr series: {seriesResponse.StatusCode} - {seriesResponse.Content}");
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
        private async Task<RestResponse> GetSonarrSeriesAsync(RestClient restClient)
        {
            var restRequest = new RestRequest("api/v3/series");
            restRequest.AddParameter("apikey", apiKey);
            return await restClient.ExecuteAsync(restRequest);
        }

        private async Task<List<SonarrTag>?> GetSonarrTagsAsync(RestClient restClient)
        {
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
