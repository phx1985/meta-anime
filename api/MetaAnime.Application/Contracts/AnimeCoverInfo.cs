namespace MetaAnime.Application.Contracts;

/// <summary>Minimal title + poster resolution result for one MAL anime id.</summary>
/// <param name="Title">Canonical MAL title.</param>
/// <param name="ImageUrl">Poster URL (main_picture large, falling back to medium). Null when unavailable.</param>
public sealed record AnimeCoverInfo(string Title, string? ImageUrl);
