namespace MetaAnime.Domain.Entities;

/// <summary>
/// Local mirror of a Sonarr "download folder imported" history event.
/// <para>
/// The activity-stats endpoint needs every import of the current year. Sonarr's
/// <c>api/v3/history</c> endpoint has to sort the whole history table on every call
/// (~10s for 2 000 records on a 34k-row history), so the cache job instead keeps this
/// append-only mirror and only asks Sonarr for the events it has not seen yet.
/// </para>
/// <para>
/// Series title and episode numbers are denormalised on purpose: the "what was added"
/// dialog must answer in milliseconds, without joining the synced catalogue (which does
/// not even cover series Sonarr tracks but MetaAnime never matched).
/// </para>
/// </summary>
public sealed class SonarrHistoryEvent
{
    /// <summary>
    /// Sonarr's own history record id, used as the primary key. It makes every merge
    /// idempotent: re-fetching an overlapping window can never duplicate a row.
    /// </summary>
    public long Id { get; set; }

    /// <summary>UTC timestamp of the import event, as reported by Sonarr.</summary>
    public DateTime OccurredAt { get; set; }

    /// <summary>Imported file size in bytes (0 when Sonarr did not report a size).</summary>
    public long SizeBytes { get; set; }

    /// <summary>Sonarr series id the episode belongs to.</summary>
    public int SeriesId { get; set; }

    /// <summary>Sonarr episode id.</summary>
    public int EpisodeId { get; set; }

    /// <summary>Series title as Sonarr reported it at import time (empty when unknown).</summary>
    public string SeriesTitle { get; set; } = string.Empty;

    /// <summary>Season number (0 for specials).</summary>
    public int SeasonNumber { get; set; }

    /// <summary>Episode number within the season.</summary>
    public int EpisodeNumber { get; set; }
}
