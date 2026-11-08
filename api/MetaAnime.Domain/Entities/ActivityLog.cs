namespace MetaAnime.Domain.Entities;

/// <summary>Kinds of events surfaced in the Activity Hub feed.</summary>
public enum ActivityType
{
    AnimeAdded = 1,
    EpisodeDownloaded = 2,
    SeasonCompleted = 3,
    SyncCompleted = 4,
    AnimeRemoved = 5,
    EpisodeWatched = 6,
    AnimeUpdated = 7
}

/// <summary>
/// Append-only activity record. Kept deliberately thin (title/subtitle + ids) so producers
/// never need a join to write an entry, and the feed can render without enrichment.
/// </summary>
public class ActivityLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public ActivityType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public int? MalId { get; set; }
    public int? TvdbId { get; set; }
    public int? SonarrSeriesId { get; set; }
    public string? PosterUrl { get; set; }
    /// <summary>info | success | warning | error — drives the card colour in the hub.</summary>
    public string Severity { get; set; } = "info";
    /// <summary>Small JSON blob for type-specific extras (duration, counts, episode code…).</summary>
    public string? MetadataJson { get; set; }
    /// <summary>
    /// Set when the anime this row added was later deleted. Reverted adds stop counting toward
    /// the net library delta, so "added then deleted" reconciles back to zero.
    /// </summary>
    public bool IsReverted { get; set; }

    /// <summary>Provider-side id of the underlying event ("sonarr-hist-98231") for idempotent ingestion.</summary>
    public string? ExternalEventKey { get; set; }

    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}
