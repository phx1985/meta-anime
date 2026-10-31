using Mediator;
using MetaAnime.Application.Contracts;
using MetaAnime.Application.Interfaces;
using MetaAnime.Application.Services;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Interfaces.Shared.Results;
using MetaAnime.Domain.Entities;

namespace MetaAnime.Application.Queries;

public sealed class GetSeiyuuNexusQueryHandler(
    IAnimeCastRepository animeCastRepository,
    IAnimeMediatorService animeMediatorService)
    : IRequestHandler<GetSeiyuuNexusQuery, Result<SeiyuuNexusResponse>>
{
    public async ValueTask<Result<SeiyuuNexusResponse>> Handle(GetSeiyuuNexusQuery request, CancellationToken cancellationToken)
    {
        try
        {
            // Lazy DB-first sync: populates AnimeCast on first open, no-op when cached.
            // Fails (CastProviderUnavailable) only when AniList is unreachable AND nothing is cached.
            var ensure = await animeMediatorService.EnsureAnimeCastForAnimeAsync(request.AnimeMalId);

            var castEntries = (await animeCastRepository.GetByAnimeMalIdAsync(request.AnimeMalId)).ToList();

            // Provider unreachable AND nothing cached: surface the failure instead of
            // pretending the anime simply has no cast.
            if (ensure.IsFailure && castEntries.Count == 0)
                return Result.Failure<SeiyuuNexusResponse>(ensure.Error);

            // Root title + poster (local MAL data first, bounded live lookup as last resort)
            var (rootTitle, rootImageUrl) = await ResolveRootCoverAsync(request.AnimeMalId);

            // Definitive empty: 200 with an empty graph and the resolved title.
            if (castEntries.Count == 0)
                return Result.Success(new SeiyuuNexusResponse(request.AnimeMalId, rootTitle, [], [], null));

            var crossLibrary = await LoadCrossLibraryAsync(request.AnimeMalId, castEntries);
            var (nodes, edges) = SeiyuuNexusGraphBuilder.Build(request.AnimeMalId, rootTitle, castEntries, crossLibrary, rootImageUrl);

            var lastUpdated = castEntries.Max(c => c.SyncedAt).ToString("yyyy-MM-ddTHH:mm:ssZ");
            return Result.Success(new SeiyuuNexusResponse(request.AnimeMalId, rootTitle, nodes, edges, lastUpdated));
        }
        catch (Exception ex)
        {
            return Result.Failure<SeiyuuNexusResponse>(new(Constants.Errors.GetSeiyuuNexusAsync, ex.Message));
        }
    }

    /// <summary>Root node cover: real title + poster when resolvable, MAL id as the title fallback.</summary>
    private async Task<(string Title, string? ImageUrl)> ResolveRootCoverAsync(string rootMalId)
    {
        var covers = await animeMediatorService.ResolveMalCoversAsync([rootMalId]);
        if (covers.IsSuccess && covers.Value.TryGetValue(rootMalId, out var info))
            return (info.Title, info.ImageUrl);

        return (rootMalId, null);
    }

    /// <summary>
    /// Collects cross-library roles for this cast and batch-resolves titles + posters.
    /// Cross nodes without a resolved title are dropped by the builder — a raw MAL id
    /// must never render as a label.
    /// </summary>
    private async Task<SeiyuuNexusGraphBuilder.CrossLibraryInfo> LoadCrossLibraryAsync(
        string rootMalId,
        IReadOnlyCollection<AnimeCast> castEntries)
    {
        var crossRoles = new Dictionary<int, List<SeiyuuNexusGraphBuilder.CrossAppearance>>(); // vaPersonId → other anime

        foreach (var vaId in castEntries.Select(c => c.VaPersonId).Distinct())
        {
            var allRoles = (await animeCastRepository.GetByVaPersonIdAsync(vaId)).ToList();
            var appearances = allRoles
                .Where(r => r.AnimeMalId != rootMalId)
                .GroupBy(r => r.AnimeMalId)
                .Select(g => new SeiyuuNexusGraphBuilder.CrossAppearance(g.Key, LanguagesOf(g)))
                .ToList();

            if (appearances.Count > 0)
                crossRoles[vaId] = appearances;
        }

        // Batch-fetch titles + posters for all cross-library anime
        var crossAnimeIds = crossRoles.Values
            .SelectMany(x => x)
            .Select(a => a.AnimeMalId)
            .Distinct()
            .ToList();

        var covers = await animeMediatorService.ResolveMalCoversAsync(crossAnimeIds);
        var animeInfo = covers.IsSuccess
            ? covers.Value.ToDictionary(kv => kv.Key, kv => new SeiyuuNexusGraphBuilder.CrossAnimeInfo(kv.Value.Title, kv.Value.ImageUrl))
            : new Dictionary<string, SeiyuuNexusGraphBuilder.CrossAnimeInfo>();

        return new SeiyuuNexusGraphBuilder.CrossLibraryInfo(crossRoles, animeInfo);
    }

    /// <summary>Distinct dub languages this VA voiced with in the other anime (comma-joined).</summary>
    private static string? LanguagesOf(IGrouping<string, AnimeCast> roles)
    {
        var languages = roles
            .Select(r => r.Language)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Distinct()
            .ToList();

        return languages.Count > 0 ? string.Join(", ", languages) : null;
    }
}
