using MetaAnime.Core.Helpers;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Interfaces.Shared.Results;
using MetaAnime.Infrastructure.Kometa.Models;
using System.Text.Json;

namespace MetaAnime.Infrastructure.Kometa.Services
{
    public class KometaAnimeJsonService : IKometaAnimeJsonService<KometaRaw>
    {
        #region Fields
        private readonly string jsonUrl = ConfigurationHelper.GetEnvironmentVariableOrThrow("KOMETA_ANIME_IDS_JSON_URL");
        #endregion

        public async Task<Result<IList<KometaRaw>>> GetAllAsync()
        {
            using var client = new HttpClient();
            using var response = await client.GetAsync(jsonUrl);

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var results = JsonSerializer.Deserialize<Dictionary<string, KometaRaw>>(json)!;

            // Initialize the AniDbId property of each KometaRawMapping object
            // with the key of the dictionary
            foreach (var (key, value) in results)
            {
                value.AniDbId = key;
            }

            return Result.Success<IList<KometaRaw>>([.. results.Values]);
        }
    }
}
