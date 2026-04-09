namespace MetaAnime.Core.Models;

/// <summary>
/// Represents a user-confirmed bridge between a Sonarr/Plex AniDbId and a MAL anime ID.
/// Persisted as a JSON array inside a single PropertyBag entry so that cross-links
/// survive the full delete-and-recreate cycle that occurs during sync.
/// </summary>
public sealed record CrossLinkEntry(string SourceAniDbId, string MalId);
