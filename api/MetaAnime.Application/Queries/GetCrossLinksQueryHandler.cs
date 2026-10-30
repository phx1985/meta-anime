using Mediator;
using MetaAnime.Application.Contracts;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Interfaces.Shared.Results;
using MetaAnime.Domain.Enums;

namespace MetaAnime.Application.Queries;

/// <summary>
/// Returns all user-confirmed AniDbId cross-links, enriched with titles.
/// All lookups are batched (single query per source) — no per-entry round-trips.
/// </summary>
public sealed class GetCrossLinksQueryHandler(
    ICrossLinkService crossLinkService,
    IAnimeMappingRepository animeMappingRepository,
    IAnimeRepository animeRepository)
    : IRequestHandler<GetCrossLinksQuery, Result<IReadOnlyList<CrossLinkResponse>>>
{
    public async ValueTask<Result<IReadOnlyList<CrossLinkResponse>>> Handle(GetCrossLinksQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var entries = await crossLinkService.GetAllAsync();
            if (entries.Count == 0)
                return Result.Success<IReadOnlyList<CrossLinkResponse>>([]);

            var aniDbIds = entries.Select(e => e.SourceAniDbId).Distinct().ToList();
            var aniDbMappings = await animeMappingRepository.GetByAniDbIdsAsync(aniDbIds);
            var aniDbTitleMap = aniDbMappings
                .Where(m => m.Anime?.AnimeSource != AnimeSource.MyAnimeList && m.Anime?.Title != null)
                .GroupBy(m => m.AniDbId)
                .ToDictionary(g => g.Key, g => g.First().Anime!.Title);

            // Batch the MAL title lookups (was one query per cross-link entry).
            var malIds = entries.Select(e => e.MalId).Distinct().ToList();
            var malAnimes = await animeRepository.GetManyBySourceIdsAsync(AnimeSource.MyAnimeList, malIds);
            var malTitleMap = malAnimes
                .GroupBy(a => a.SourceId)
                .ToDictionary(g => g.Key, g => g.First().Title);

            var dtos = new List<CrossLinkResponse>(entries.Count);
            foreach (var e in entries)
            {
                malTitleMap.TryGetValue(e.MalId, out var malTitle);
                aniDbTitleMap.TryGetValue(e.SourceAniDbId, out var aniDbTitle);
                dtos.Add(new CrossLinkResponse(e.SourceAniDbId, aniDbTitle, e.MalId, malTitle));
            }

            return Result.Success<IReadOnlyList<CrossLinkResponse>>(dtos);
        }
        catch (Exception ex)
        {
            return Result.Failure<IReadOnlyList<CrossLinkResponse>>(new(Constants.Errors.GetCrossLinks, ex.Message));
        }
    }
}
