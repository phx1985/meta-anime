namespace MetaAnime.Application.Contracts;

/// <summary>Result returned when a series is successfully added to Sonarr.</summary>
public sealed record SonarrAddResult(int SonarrId, string Title, string? TitleSlug, string? SonarrUrl);
