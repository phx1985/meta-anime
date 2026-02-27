using LukeHagar.PlexAPI.SDK;
using LukeHagar.PlexAPI.SDK.Models.Requests;
using MetaAnime.Core;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Interfaces.Shared.Results;
using MetaAnime.Core.Settings;
using MetaAnime.Infrastructure.Plex.Models;
using MetaAnime.Infrastructure.Plex.Utilities;
using Microsoft.Extensions.Options;
using Components = LukeHagar.PlexAPI.SDK.Models.Components;

namespace MetaAnime.Infrastructure.Plex.Services;

public sealed class PlexAnimeSourceService(IOptions<PlexSettings> options, IPropertyBagRepository propertyBagRepository) : IPlexAnimeSourceService<PlexMedia>
{
    private (IPlexAPI plexAPI, int[] animeSections, bool isConfigured)? _settings;

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

    private async Task<(IPlexAPI plexAPI, int[] animeSections, bool isConfigured)> GetSettingsAsync()
    {
        if (_settings.HasValue) return _settings.Value;

        var hostEntry = await propertyBagRepository.GetByKeyAsync(SettingsKeys.PlexHost);
        var tokenEntry = await propertyBagRepository.GetByKeyAsync(SettingsKeys.PlexToken);
        var sectionsEntry = await propertyBagRepository.GetByKeyAsync(SettingsKeys.PlexAnimeSections);

        var host = hostEntry?.Value ?? options.Value.Host ?? string.Empty;
        var token = tokenEntry?.Value ?? options.Value.Token ?? string.Empty;
        var sectionsStr = sectionsEntry?.Value ?? options.Value.AnimeSections ?? string.Empty;
        var animeSections = ParseAnimeSections(sectionsStr);
        var isConfigured = !string.IsNullOrWhiteSpace(host)
            && !string.IsNullOrWhiteSpace(token)
            && animeSections.Length > 0;

        var plexAPI = PlexAPI.Builder()
            .WithToken(token)
            .WithServerUrl(host)
            .WithClient(new SpeakeasyHttpClient2())
            .Build();

        _settings = (plexAPI, animeSections, isConfigured);
        return _settings.Value;
    }

    public async Task<Result<IList<PlexMedia>>> GetAllAsync()
    {
        var (plexAPI, animeSections, isConfigured) = await GetSettingsAsync();
        var results = new List<PlexMedia>();

        if (!isConfigured)
        {
            return Result.Success<IList<PlexMedia>>(results);
        }

        var showRequest = new ListContentRequest
        {
            SectionId = animeSections[0].ToString(),
            MediaQuery = new Components.MediaQuery
            {
                Type = Components.MediaType.TvShow
            },
            XPlexContainerSize = null,
            IncludeMeta = Components.BoolInt.True,
            IncludeGuids = Components.BoolInt.True
        };
        var res = await plexAPI.Content.ListContentAsync(showRequest);

        res.RawResponse.EnsureSuccessStatusCode();

        var mediaContainer = res.MediaContainerWithMetadata?.MediaContainer;
        if (mediaContainer?.Metadata == null)
        {
            return Result.Success<IList<PlexMedia>>(results);
        }

        results.AddRange(mediaContainer.Metadata.Select(MapToPlexMedia));

        return Result.Success<IList<PlexMedia>>(results);
    }

    public async Task<Result<PlexMedia>> GetByIdAsync(string id)
    {
        var (plexAPI, _, isConfigured) = await GetSettingsAsync();

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
        var (plexAPI, _, isConfigured) = await GetSettingsAsync();

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

        return Result.Success(machineIdentifier);
    }

    #region Private Methods
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
}
