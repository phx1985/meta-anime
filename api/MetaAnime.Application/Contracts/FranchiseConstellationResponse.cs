namespace MetaAnime.Application.Contracts;

public sealed record FranchiseRelationDto(
    string MalId,
    string Title,
    string? CoverImageUrl,
    string? RelationType,
    string? RelationTypeFormatted
);

public sealed record FranchiseConstellationResponse(
    string MalId,
    string Title,
    string? CoverImageUrl,
    IEnumerable<FranchiseRelationDto> Relations
);
