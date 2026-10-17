using Mediator;
using MetaAnime.Application.Contracts;
using MetaAnime.Application.Interfaces;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Interfaces.Shared.Results;
using MetaAnime.Domain.Enums;

namespace MetaAnime.Application.Queries;

public sealed class GetSeiyuuNexusQueryHandler(
    IAnimeCastRepository animeCastRepository,
    IAnimeRepository animeRepository,
    IAnimeMediatorService animeMediatorService)
    : IRequestHandler<GetSeiyuuNexusQuery, Result<SeiyuuNexusResponse>>
{
    public async ValueTask<Result<SeiyuuNexusResponse>> Handle(GetSeiyuuNexusQuery request, CancellationToken cancellationToken)
    {
        try
        {
            // Lazy fetch: populate AnimeCast for this anime on first open; no-op if already cached
            await animeMediatorService.EnsureAnimeCastForAnimeAsync(request.AnimeMalId);

            var castEntries = (await animeCastRepository.GetByAnimeMalIdAsync(request.AnimeMalId)).ToList();

            // Always resolve the root anime title even for the empty-cast case
            var rootAnime = await animeRepository.GetAnimeAsync(AnimeSource.MyAnimeList, request.AnimeMalId);
            var rootTitle = rootAnime?.Title ?? request.AnimeMalId;

            if (castEntries.Count == 0)
            {
                return Result.Success(new SeiyuuNexusResponse(
                    request.AnimeMalId, rootTitle, [], [], null));
            }

            var nodes = new List<NexusNodeDto>();
            var edges = new List<NexusEdgeDto>();

            // Root anime node
            var animeNodeId = $"anime:{request.AnimeMalId}";
            nodes.Add(new NexusNodeDto(animeNodeId, "anime", rootTitle, null, true, request.AnimeMalId, null, null));

            var addedChars = new HashSet<int>();
            var addedVas   = new HashSet<int>();

            // Collect cross-library anime the VAs in this cast also appear in
            var allVaIds = castEntries.Select(c => c.VaPersonId).Distinct().ToList();
            var crossRoles = new Dictionary<int, List<string>>(); // vaPersonId → other animeMalIds

            foreach (var vaId in allVaIds)
            {
                var allRoles = await animeCastRepository.GetByVaPersonIdAsync(vaId);
                var otherAnimeIds = allRoles
                    .Select(r => r.AnimeMalId)
                    .Where(id => id != request.AnimeMalId)
                    .Distinct()
                    .ToList();
                if (otherAnimeIds.Count > 0)
                    crossRoles[vaId] = otherAnimeIds;
            }

            // Batch-fetch titles for all cross-library anime in one query
            var crossAnimeIds = crossRoles.Values.SelectMany(x => x).Distinct().ToList();
            var crossAnimeTitles = crossAnimeIds.Count > 0
                ? (await animeRepository.GetManyBySourceIdsAsync(AnimeSource.MyAnimeList, crossAnimeIds))
                    .ToDictionary(a => a.SourceId, a => a.Title)
                : new Dictionary<string, string>();

            foreach (var cast in castEntries)
            {
                // Character node
                if (addedChars.Add(cast.CharacterId))
                {
                    var charNodeId = $"char:{cast.CharacterId}";
                    nodes.Add(new NexusNodeDto(charNodeId, "character", cast.CharacterName, cast.CharacterImageUrl, false, null, null, cast.CharacterRole));
                    edges.Add(new NexusEdgeDto(animeNodeId, charNodeId, "hasCharacter", null));
                }

                // VA node
                if (addedVas.Add(cast.VaPersonId))
                {
                    var vaNodeId = $"va:{cast.VaPersonId}";
                    nodes.Add(new NexusNodeDto(vaNodeId, "va", cast.VaName, cast.VaImageUrl, false, null, cast.VaPersonId, null));
                }

                edges.Add(new NexusEdgeDto($"va:{cast.VaPersonId}", $"char:{cast.CharacterId}", "voices", cast.Language));
            }

            // Add cross-library anime nodes and edges
            var addedCrossAnime = new HashSet<string>();
            foreach (var (vaId, otherIds) in crossRoles)
            {
                foreach (var otherId in otherIds)
                {
                    var otherAnimeNodeId = $"anime:{otherId}";
                    if (addedCrossAnime.Add(otherId))
                    {
                        var otherTitle = crossAnimeTitles.GetValueOrDefault(otherId, otherId);
                        nodes.Add(new NexusNodeDto(otherAnimeNodeId, "anime", otherTitle, null, false, otherId, null, null));
                    }
                    edges.Add(new NexusEdgeDto($"va:{vaId}", otherAnimeNodeId, "appearsIn", null));
                }
            }

            var lastUpdated = castEntries.Max(c => c.SyncedAt).ToString("yyyy-MM-ddTHH:mm:ssZ");

            return Result.Success(new SeiyuuNexusResponse(
                request.AnimeMalId,
                rootTitle,
                nodes,
                edges,
                lastUpdated));
        }
        catch (Exception ex)
        {
            return Result.Failure<SeiyuuNexusResponse>(new(Constants.Errors.GetSeiyuuNexusAsync, ex.Message));
        }
    }
}
