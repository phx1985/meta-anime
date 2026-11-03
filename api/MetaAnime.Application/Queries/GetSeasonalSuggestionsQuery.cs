using Mediator;
using MetaAnime.Application.Contracts;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Queries;

/// <summary>
/// <paramref name="Scope"/>: "current" | "last2" | "last4" | "custom" (custom ranges are capped to 4 seasons).
/// <paramref name="ExcludeGenres"/> is a comma-separated tag list; any candidate carrying one of those tags is dropped.
/// </summary>
public record GetSeasonalSuggestionsQuery(
    int Year,
    string Season,
    int Limit = 16,
    string Scope = "current",
    int? FromYear = null,
    string? FromSeason = null,
    int? ToYear = null,
    string? ToSeason = null,
    bool ExcludeChinese = false,
    string? ExcludeGenres = null)
    : IRequest<Result<IEnumerable<SeasonalSuggestionResponse>>>;
