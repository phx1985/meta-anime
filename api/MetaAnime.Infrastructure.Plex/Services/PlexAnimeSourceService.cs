using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using LukeHagar.PlexAPI.SDK;
using LukeHagar.PlexAPI.SDK.Models.Requests;
using MetaAnime.Core;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Interfaces.Shared.Results;
using MetaAnime.Infrastructure.Plex.Models;
using MetaAnime.Infrastructure.Plex.Utilities;
using Components = LukeHagar.PlexAPI.SDK.Models.Components;

namespace MetaAnime.Infrastructure.Plex.Services;

public sealed class PlexAnimeSourceService(
    IPropertyBagRepository propertyBagRepository) : IPlexAnimeSourceService<PlexMedia>
{
    /// <summary>
    /// JSON options for Plex API responses.
    /// Case-sensitive matching is intentional: Plex returns BOTH "guid" (lowercase string,
    /// the item's own Plex URI) and "Guid" (PascalCase array of external IDs).
    /// Case-insensitive mode causes a JsonException because both names collapse to the same
    /// property and the deserializer tries to put the string value into List&lt;PlexGuid&gt;.
    /// All DTOs use explicit [JsonPropertyName] attributes instead.
    /// </summary>
    private static readonly JsonSerializerOptions PlexJsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
    };

    private static int[] ParseAnimeSections(string sections)
    {
        if (string.IsNullOrWhiteSpace(sections))
            return Array.Empty<int>();

        try
        {
            return sections.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => int.TryParse(s, out _))
                .Select(int.Parse)
                .ToArray();
        }
        catch
        {
            return Array.Empty<int>();
        }
    }

    private async Task<(IPlexAPI plexAPI, string host, string token, int[] animeSections, bool isConfigured)> GetSettingsAsync()
    {
        var hostEntry = await propertyBagRepository.GetByKeyAsync(SettingsKeys.PlexHost);
        var tokenEntry = await propertyBagRepository.GetByKeyAsync(SettingsKeys.PlexToken);
        var sectionsEntry = await propertyBagRepository.GetByKeyAsync(SettingsKeys.PlexAnimeSections);

        var host = hostEntry?.Value ?? string.Empty;
        var token = tokenEntry?.Value ?? string.Empty;
        var sectionsStr = sectionsEntry?.Value ?? string.Empty;
        var animeSections = ParseAnimeSections(sectionsStr);
        var isConfigured = !string.IsNullOrWhiteSpace(host)
            && !string.IsNullOrWhiteSpace(token)
            && animeSections.Length > 0;

        var plexAPI = PlexAPI.Builder()
            .WithToken(token)
            .WithServerUrl(host)
            .WithClient(new SpeakeasyHttpClient2(token))
            .Build();

        return (plexAPI, host, token, animeSections, isConfigured);
    }

    public async Task<Result<IList<PlexMedia>>> GetAllAsync()
    {
        var (_, host, token, animeSections, isConfigured) = await GetSettingsAsync();
        List<PlexMedia> results = [];

        if (!isConfigured)
        {
            return Result.Success<IList<PlexMedia>>(results);
        }

        // Use a dedicated handler + client with a generous timeout for large Plex library dumps.
        // The global Aspire AddStandardResilienceHandler applies a 30-second total-request timeout
        // to every IHttpClientFactory client; we bypass it here by creating the client directly.
        using var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };
        using var httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(5),
        };
        httpClient.DefaultRequestHeaders.Add("X-Plex-Token", token);
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        foreach (var sectionId in animeSections)
        {
            var url = $"{host.TrimEnd('/')}/library/sections/{sectionId}/all?type=2&includeGuids=1";
            try
            {
                // ResponseHeadersRead: start reading the JSON stream as soon as the first byte
                // arrives — avoids buffering the entire payload into memory before deserialization.
                using var httpResponse = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                httpResponse.EnsureSuccessStatusCode();
                await using var stream = await httpResponse.Content.ReadAsStreamAsync();
                var sectionResponse = await JsonSerializer.DeserializeAsync<PlexSectionAllResponse>(stream, PlexJsonOptions);
                var metadata = sectionResponse?.MediaContainer?.Metadata;
                if (metadata == null) continue;

                results.AddRange(metadata.Select(MapToPlexMedia));
            }
            catch
            {
                // Skip sections that fail to load rather than aborting the entire sync
                continue;
            }
        }

        return Result.Success<IList<PlexMedia>>(results);
    }

    public async Task<Result<PlexMedia>> GetByIdAsync(string id)
    {
        var (plexAPI, _, _, _, isConfigured) = await GetSettingsAsync();

        if (!isConfigured)
        {
            return Result.Failure<PlexMedia>(new(Constants.Errors.NotConfigured, "Plex is not configured"));
        }

        var req = new GetMetadataItemRequest { Ids = [id] };
        var response = await plexAPI.Content.GetMetadataItemAsync(req);

        var mediaContainer = response.MediaContainerWithMetadata?.MediaContainer;
        if (mediaContainer?.Metadata == null || mediaContainer.Metadata.Count == 0)
        {
            return Result.Success<PlexMedia>(null!);
        }

        var result = MapToPlexMedia(mediaContainer.Metadata.First());
        return Result.Success(result!);
    }

    public async Task<Result<string>> GetMachineIdentifierAsync()
    {
        // Return the cached value immediately — avoids a live Plex SDK round-trip on every request
        // and guarantees the ID is available even when the local Plex server is temporarily offline.
        var cached = await propertyBagRepository.GetByKeyAsync(SettingsKeys.PlexMachineIdentifier);
        if (!string.IsNullOrWhiteSpace(cached?.Value))
            return Result.Success(cached.Value);

        var (plexAPI, _, _, _, isConfigured) = await GetSettingsAsync();

        if (!isConfigured)
        {
            return Result.Failure<string>(new Error(Constants.Errors.NotConfigured, "Plex is not configured"));
        }

        var response = await plexAPI.General.GetIdentityAsync();
        var machineIdentifier = response.Object?.MediaContainer?.MachineIdentifier;

        if (string.IsNullOrEmpty(machineIdentifier))
        {
            return Result.Failure<string>(new Error(Constants.Errors.MachineIdentifierNotFound, "Could not retrieve machine identifier."));
        }

        // Persist so future calls (and the settings API) can read it without hitting Plex each time.
        await propertyBagRepository.UpsertAsync(SettingsKeys.PlexMachineIdentifier, machineIdentifier);
        return Result.Success(machineIdentifier);
    }

    public async Task<Result<IList<PlexLibraryInfo>>> GetLibrariesAsync()
    {
        var tokenEntry = await propertyBagRepository.GetByKeyAsync(SettingsKeys.PlexToken);
        var hostEntry = await propertyBagRepository.GetByKeyAsync(SettingsKeys.PlexHost);

        var token = tokenEntry?.Value ?? string.Empty;
        var host = hostEntry?.Value ?? string.Empty;

        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(host))
        {
            return Result.Failure<IList<PlexLibraryInfo>>(new Error(Constants.Errors.NotConfigured, "Plex token or server URL is not configured."));
        }

        var plexAPI = PlexAPI.Builder()
            .WithToken(token)
            .WithServerUrl(host)
            .WithClient(new SpeakeasyHttpClient2(token))
            .Build();

        var response = await plexAPI.Library.GetSectionsAsync();
        var sections = response.Object?.MediaContainer?.Directory;

        if (sections == null)
        {
            return Result.Success<IList<PlexLibraryInfo>>([]);
        }

        var libraries = sections
            .Select(s => new PlexLibraryInfo(s.Key ?? string.Empty, s.Title ?? string.Empty, s.Type.ToString() ?? string.Empty))
            .ToList<PlexLibraryInfo>();

        return Result.Success<IList<PlexLibraryInfo>>(libraries);
    }

    #region Private Methods
    private static PlexMedia MapToPlexMedia(PlexMetadataItem metadata)
        => new()
        {
            Description = metadata.Summary ?? string.Empty,
            Id = metadata.RatingKey ?? string.Empty,
            Title = metadata.Title ?? string.Empty,
            ExternalGuilds = metadata.ExternalGuids?.Select(g => g.Id ?? string.Empty).ToArray() ?? [],
            ViewedLeafCount = metadata.ViewedLeafCount ?? 0,
            LeafCount = metadata.LeafCount ?? 0
        };

    private static PlexMedia MapToPlexMedia(Components.Metadata metadata)
        => new()
        {
            Description = metadata.Summary ?? string.Empty,
            Id = metadata.RatingKey ?? string.Empty,
            Title = metadata.Title ?? string.Empty,
            ExternalGuilds = metadata?.Guids?.Select(g => g.Id).ToArray() ?? [],
            ViewedLeafCount = metadata?.ViewedLeafCount ?? 0,
            LeafCount = metadata?.LeafCount ?? 0
        };
    #endregion

    #region Direct HTTP response DTOs
    // Plex's JSON has a deliberate case collision:
    //   "guid"  (lowercase) → a single string — the item's own internal Plex URI
    //   "Guid"  (PascalCase) → an array of external-ID objects (tvdb://, anidb://, etc.)
    // Every property uses an explicit [JsonPropertyName] so case-sensitive deserialisation
    // maps each field to exactly the right C# property with no ambiguity.
    private sealed record PlexSectionAllResponse(
        [property: JsonPropertyName("MediaContainer")] PlexMetadataContainer? MediaContainer);

    private sealed record PlexMetadataContainer(
        [property: JsonPropertyName("Metadata")] List<PlexMetadataItem>? Metadata);

    private sealed record PlexMetadataItem(
        [property: JsonPropertyName("ratingKey")]       string? RatingKey,
        [property: JsonPropertyName("title")]           string? Title,
        [property: JsonPropertyName("summary")]         string? Summary,
        [property: JsonPropertyName("guid")]            string? PlexInternalGuid,
        [property: JsonPropertyName("Guid")]            List<PlexGuid>? ExternalGuids,
        [property: JsonPropertyName("viewedLeafCount")] int? ViewedLeafCount,
        [property: JsonPropertyName("leafCount")]       int? LeafCount);

    private sealed record PlexGuid(
        [property: JsonPropertyName("id")] string? Id);
    #endregion
}
