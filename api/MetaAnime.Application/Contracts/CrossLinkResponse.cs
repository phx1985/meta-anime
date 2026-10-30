namespace MetaAnime.Application.Contracts;

/// <summary>Cross-link entry enriched with human-readable titles for display.</summary>
public sealed record CrossLinkResponse(
    string SourceAniDbId,
    string? AniDbTitle,
    string MalId,
    string? MalTitle);
