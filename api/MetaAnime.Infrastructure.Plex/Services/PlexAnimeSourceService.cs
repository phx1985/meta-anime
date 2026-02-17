using LukeHagar.PlexAPI.SDK;
using LukeHagar.PlexAPI.SDK.Models.Requests;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Interfaces.Shared.Results;
using MetaAnime.Core.Settings;
using MetaAnime.Infrastructure.Plex.Models;
using Microsoft.Extensions.Options;
using Components = LukeHagar.PlexAPI.SDK.Models.Components;

namespace MetaAnime.Infrastructure.Plex.Services
{
    public class PlexAnimeSourceService(IPlexAPI plexAPI, IOptions<PlexSettings> options) : IPlexAnimeSourceService<PlexMedia>
    {
        #region Fields
        private readonly int[] animeSections = [.. options.Value.AnimeSections
                                               .Split(',')
                                               .Select(int.Parse)];
        private readonly IPlexAPI plexAPI = plexAPI;
        #endregion

        public async Task<Result<IList<PlexMedia>>> GetAllAsync()
        {
            var results = new List<PlexMedia>();
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
            var req = new GetMetadataItemRequest { Ids = [id] }; // Correct usage
            var response = await plexAPI.Content.GetMetadataItemAsync(req);

            var mediaContainer = response.MediaContainerWithMetadata?.MediaContainer;
            if (mediaContainer?.Metadata == null || mediaContainer.Metadata.Count == 0)
            {
                 // Handle null?
                 return Result.Success<PlexMedia>(null!);
            }

            var result = MapToPlexMedia(mediaContainer.Metadata.First());
            return Result.Success(result!);
        }

        public async Task<Result<string>> GetMachineIdentifierAsync()
        {
            var response = await plexAPI.General.GetIdentityAsync();
            var machineIdentifier = response.Object?.MediaContainer?.MachineIdentifier;

            if (string.IsNullOrEmpty(machineIdentifier))
            {
                return Result.Failure<string>(new Error("MachineIdentifierNotFound", "Could not retrieve machine identifier."));
            }

            return Result.Success(machineIdentifier);
        }

        #region Private Methods
        private static PlexMedia MapToPlexMedia(Components.Metadata metadata)
            => new()
            {
                Description = metadata.Summary,
                Id = metadata.RatingKey,
                Title = metadata.Title,
                ExternalGuilds = metadata?.Guids?.Select(g => g.Id).ToArray() ?? []
            };
        #endregion
    }
}
