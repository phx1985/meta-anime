namespace MetaAnime.Application.Contracts;

/// <summary>A resolved deep link to the item inside one provider.</summary>
public sealed record ActivityProviderLinkDto(string Provider, string Label, string ExternalUrl);

/// <summary>One row of the Activity Hub feed.</summary>
public sealed record ActivityLogDto(
    Guid Id,
    string Type,
    string Title,
    string? Subtitle,
    int? MalId,
    int? TvdbId,
    int? SonarrSeriesId,
    string? PosterUrl,
    string Severity,
    bool IsReverted,
    IReadOnlyList<ActivityProviderLinkDto> ProviderLinks,
    string? MetadataJson,
    DateTimeOffset Timestamp,
    /// <summary>Internal anime id (when the row maps to a library entry) so the UI can open the
    /// detail drawer even for shows with no MAL id.</summary>
    Guid? AnimeId = null);

/// <summary>Offset-paged feed payload (<c>page</c> is 1-based).</summary>
public sealed record ActivityFeedResponse(
    IReadOnlyList<ActivityLogDto> Items,
    int Total,
    int Page,
    int PageSize);
