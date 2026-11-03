using MetaAnime.Application.Contracts;
using MetaAnime.Domain.Entities;

namespace MetaAnime.Application.Services;

/// <summary>
/// Pure builder for the Seiyuu Nexus graph from persisted cast rows: root anime node +
/// character and VA nodes, <c>hasCharacter</c>/<c>voices</c> edges (voice edge label = language),
/// and cross-library <c>appearsIn</c> edges (VAs → other anime in the user's library) with
/// batch-resolved titles and posters. Works entirely from DB rows, so cached graphs render offline.
/// </summary>
public static class SeiyuuNexusGraphBuilder
{
    /// <summary>One VA's role in another anime: target MAL id + the dub language(s) voiced there.</summary>
    /// <param name="AnimeMalId">MAL id of the other anime.</param>
    /// <param name="Language">Distinct dub languages for this VA in that anime (comma-separated). Null when unknown.</param>
    public sealed record CrossAppearance(string AnimeMalId, string? Language);

    /// <summary>Resolved title + poster for one cross-library anime node.</summary>
    public sealed record CrossAnimeInfo(string Title, string? ImageUrl);

    /// <summary>Cross-library lookup data prepared by the caller (batched title/poster resolution).</summary>
    /// <param name="VaToOtherAnimeIds">vaPersonId → other anime appearances (root anime excluded).</param>
    /// <param name="AnimeInfo">anime MAL id → resolved title + poster. Ids missing here are dropped.</param>
    public sealed record CrossLibraryInfo(
        IReadOnlyDictionary<int, List<CrossAppearance>> VaToOtherAnimeIds,
        IReadOnlyDictionary<string, CrossAnimeInfo> AnimeInfo);

    public static (List<NexusNodeDto> Nodes, List<NexusEdgeDto> Edges) Build(
        string rootMalId,
        string rootTitle,
        IReadOnlyCollection<AnimeCast> castEntries,
        CrossLibraryInfo crossLibrary,
        string? rootImageUrl = null)
    {
        var nodes = new List<NexusNodeDto>();
        var edges = new List<NexusEdgeDto>();

        // Root anime node
        var animeNodeId = $"anime:{rootMalId}";
        nodes.Add(new NexusNodeDto(animeNodeId, "anime", rootTitle, rootImageUrl, true, rootMalId, null, null));

        var addedChars = new HashSet<int>();
        var addedVas = new HashSet<int>();

        foreach (var cast in castEntries)
        {
            // Character node + hasCharacter edge
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

            // voices edge, label = language
            edges.Add(new NexusEdgeDto($"va:{cast.VaPersonId}", $"char:{cast.CharacterId}", "voices", cast.Language));
        }

        // Cross-library: each VA also appears in other anime already in the library.
        // Nodes without a resolved title are dropped entirely — a raw MAL id must never
        // leak into a rendered label.
        var addedCrossAnime = new HashSet<string>();
        foreach (var (vaId, appearances) in crossLibrary.VaToOtherAnimeIds)
        {
            foreach (var appearance in appearances)
            {
                if (!crossLibrary.AnimeInfo.TryGetValue(appearance.AnimeMalId, out var info))
                    continue;

                var otherAnimeNodeId = $"anime:{appearance.AnimeMalId}";
                if (addedCrossAnime.Add(appearance.AnimeMalId))
                {
                    nodes.Add(new NexusNodeDto(otherAnimeNodeId, "anime", info.Title, info.ImageUrl, false, appearance.AnimeMalId, null, null));
                }

                var language = string.IsNullOrWhiteSpace(appearance.Language) ? null : appearance.Language;
                edges.Add(new NexusEdgeDto($"va:{vaId}", otherAnimeNodeId, "appearsIn", language));
            }
        }

        return (nodes, edges);
    }
}
