using MetaAnime.Core;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Interfaces.Shared.Results;
using MetaAnime.Core.Settings;
using MetaAnime.Infrastructure.Kometa.Models;
using Microsoft.Extensions.Options;
using System.Net.Http;
using System.Text.Json;

namespace MetaAnime.Infrastructure.Kometa.Services;

public sealed class KometaAnimeJsonService(
    IHttpClientFactory httpClientFactory,
    IOptions<KometaSettings> options,
    IPropertyBagRepository propertyBagRepository) : IKometaAnimeJsonService<KometaRaw>
{
    private const string DefaultAnimeIdsJsonUrl = KometaSettings.DefaultAnimeIdsJsonUrl;
    private const string ClientName = "Kometa";

    public async Task<Result<IList<KometaRaw>>> GetAllAsync()
    {
        var urlEntry = await propertyBagRepository.GetByKeyAsync(SettingsKeys.KometaAnimeIdsJsonUrl);
        var jsonUrl = urlEntry?.Value
            ?? options.Value.AnimeIdsJsonUrl
            ?? DefaultAnimeIdsJsonUrl;

        try
        {
            using var client = httpClientFactory.CreateClient(ClientName);
            using var response = await client.GetAsync(jsonUrl);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var results = JsonSerializer.Deserialize<Dictionary<string, KometaRaw>>(json);

            if (results is null)
                return Result.Failure<IList<KometaRaw>>(
                    new Error("Kometa.EmptyResponse", "The Kometa JSON source returned an empty or null mapping."));

            foreach (var (key, value) in results)
                value.AniDbId = key;

            return Result.Success<IList<KometaRaw>>([.. results.Values]);
        }
        catch (HttpRequestException ex)
        {
            return Result.Failure<IList<KometaRaw>>(
                new Error("Kometa.HttpError", $"Failed to fetch Kometa JSON from '{jsonUrl}': {ex.Message}"));
        }
        catch (JsonException ex)
        {
            return Result.Failure<IList<KometaRaw>>(
                new Error("Kometa.ParseError", $"Failed to parse Kometa JSON: {ex.Message}"));
        }
    }
}
