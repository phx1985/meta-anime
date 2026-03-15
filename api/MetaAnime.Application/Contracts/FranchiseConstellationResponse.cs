namespace MetaAnime.Application.Contracts;

public sealed record FranchiseRelationDto(
    string MalId,
    string Title,
    string? CoverImageUrl,
    string? RelationType,
    string? RelationTypeFormatted,
    string? MediaType,
    string? AiringStatus,
    string? WatchingStatus
);

public sealed record FranchiseConstellationResponse(
    string MalId,
    string Title,
    string? CoverImageUrl,
    string? Synopsis,
    string? StartDate,
    string? EndDate,
    IEnumerable<FranchiseRelationDto> Relations,
    string? MediaType,
    string? AiringStatus,
    string? WatchingStatus
);
