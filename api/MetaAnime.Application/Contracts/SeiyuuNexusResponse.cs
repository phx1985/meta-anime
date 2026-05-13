namespace MetaAnime.Application.Contracts;

/// <summary>A single node in the Seiyuu Nexus graph.</summary>
public sealed record NexusNodeDto(
    /// <summary>Unique identifier within the graph. Format: "anime:{malId}", "char:{charId}", "va:{personId}".</summary>
    string Id,
    /// <summary>anime | character | va</summary>
    string Type,
    string Label,
    string? ImageUrl,
    /// <summary>True only for the starting anime node.</summary>
    bool IsRoot,
    /// <summary>For anime nodes: the MAL ID (to open detail panel on click).</summary>
    string? MalId,
    /// <summary>For VA nodes: the MAL person ID (to load profile on focus).</summary>
    int? PersonId,
    /// <summary>For character nodes: Main or Supporting.</summary>
    string? Role
);

/// <summary>A directed edge between two nodes in the Seiyuu Nexus graph.</summary>
public sealed record NexusEdgeDto(
    string FromId,
    string ToId,
    /// <summary>voices | appearsIn | hasCharacter</summary>
    string Relation,
    /// <summary>The dub language for "voices" edges (e.g. "Japanese", "English"). Null for other edge types.</summary>
    string? Language
);

/// <summary>Full response for the Seiyuu Nexus graph.</summary>
public sealed record SeiyuuNexusResponse(
    string RootMalId,
    string RootAnimeTitle,
    IEnumerable<NexusNodeDto> Nodes,
    IEnumerable<NexusEdgeDto> Edges,
    /// <summary>ISO UTC timestamp when cast data was last synced for this anime. Null if never synced.</summary>
    string? LastUpdated
);
