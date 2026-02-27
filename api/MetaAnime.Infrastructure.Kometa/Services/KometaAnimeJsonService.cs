using MetaAnime.Core;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Interfaces.Shared.Results;
using MetaAnime.Core.Settings;
using MetaAnime.Infrastructure.Kometa.Models;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace MetaAnime.Infrastructure.Kometa.Services;

public sealed class KometaAnimeJsonService(IOptions<KometaSettings> options, IPropertyBagRepository propertyBagRepository) : IKometaAnimeJsonService<KometaRaw>
{
    private const string DefaultAnimeIdsJsonUrl = "https://raw.githubusercontent.com/Kometa-Team/Anime-IDs/refs/heads/master/anime_ids.json";

    public async Task<Result<IList<KometaRaw>>> GetAllAsync()
    {
        var urlEntry = await propertyBagRepository.GetByKeyAsync(SettingsKeys.KometaAnimeIdsJsonUrl);
        var jsonUrl = urlEntry?.Value
            ?? options.Value.AnimeIdsJsonUrl
            ?? DefaultAnimeIdsJsonUrl;

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
