using MetaAnime.Core;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Interfaces.Shared.Results;
using MetaAnime.Core.Models;
using MetaAnime.Core.Settings;
using MetaAnime.Infrastructure.Kometa.Models;
using Microsoft.Extensions.Options;
using System.Net;
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
        var outcome = await FetchAsync(etag: null, lastModified: null, forceRefresh: true);
        if (outcome.IsFailure)
            return Result.Failure<IList<KometaRaw>>(outcome.Error);

        return Result.Success(outcome.Value.Items!);
    }

    public async Task<Result<KometaFetchOutcome<KometaRaw>>> GetIfModifiedAsync(string? etag, string? lastModified, bool forceRefresh = false)
        => await FetchAsync(etag, lastModified, forceRefresh);

    private async Task<Result<KometaFetchOutcome<KometaRaw>>> FetchAsync(string? etag, string? lastModified, bool forceRefresh)
    {
        var urlEntry = await propertyBagRepository.GetByKeyAsync(SettingsKeys.KometaAnimeIdsJsonUrl);
        var jsonUrl = urlEntry?.Value
            ?? options.Value.AnimeIdsJsonUrl
            ?? DefaultAnimeIdsJsonUrl;

        try
        {
            using var client = httpClientFactory.CreateClient(ClientName);
            using var request = new HttpRequestMessage(HttpMethod.Get, jsonUrl);
            if (!forceRefresh)
            {
                if (!string.IsNullOrWhiteSpace(etag))
                    request.Headers.TryAddWithoutValidation("If-None-Match", etag);
                if (!string.IsNullOrWhiteSpace(lastModified))
                    request.Headers.TryAddWithoutValidation("If-Modified-Since", lastModified);
            }

            using var response = await client.SendAsync(request);

            if (response.StatusCode == HttpStatusCode.NotModified)
                return Result.Success(new KometaFetchOutcome<KometaRaw>(NotModified: true, Items: null, etag, lastModified));

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var results = JsonSerializer.Deserialize<Dictionary<string, KometaRaw>>(json);

            if (results is null)
                return Result.Failure<KometaFetchOutcome<KometaRaw>>(
                    new Error("Kometa.EmptyResponse", "The Kometa JSON source returned an empty or null mapping."));

            foreach (var (key, value) in results)
                value.AniDbId = key;

            return Result.Success(new KometaFetchOutcome<KometaRaw>(
                NotModified: false,
                Items: [.. results.Values],
                ETag: response.Headers.ETag?.Tag,
                LastModified: response.Content.Headers.LastModified?.ToString("R")));
        }
        catch (HttpRequestException ex)
        {
            return Result.Failure<KometaFetchOutcome<KometaRaw>>(
                new Error("Kometa.HttpError", $"Failed to fetch Kometa JSON from '{jsonUrl}': {ex.Message}"));
        }
        catch (JsonException ex)
        {
            return Result.Failure<KometaFetchOutcome<KometaRaw>>(
                new Error("Kometa.ParseError", $"Failed to parse Kometa JSON: {ex.Message}"));
        }
    }
}
