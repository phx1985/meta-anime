namespace MetaAnime.Core.Models.BotKun;

public sealed class BotKunChatRequest
{
    public required string UserMessage { get; init; }
    public IList<ChatMessage> History { get; init; } = [];
    public string Language { get; init; } = "en";

    // R22: explicit pagination context so the service can reliably page through
    // the same query without relying solely on the LLM's page extraction.
    // The client sends the last page it received; the service adds 1 when appropriate.
    // Kept for backward compatibility — the signed cursor below is the preferred path.
    public int? PreviousSearchPage { get; init; }
    public string? PreviousSearchQuery { get; init; }

    /// <summary>
    /// Deterministic fast-path action set by explicit UI controls.
    /// "lucky" = Feeling Lucky pick (skips LLM intent extraction entirely);
    /// "more"  = next page of the previous search (requires <see cref="NextPageCursor"/>).
    /// Null/unknown values fall back to the normal intent-extraction pipeline.
    /// </summary>
    public string? Action { get; init; }

    /// <summary>
    /// Opaque server-signed search cursor emitted as a [PC:…] stream chunk on DB-path
    /// responses. Carries the exact AnimeFilters + page that produced the current
    /// results, so "more" never re-runs (or mutates) the LLM's filter extraction.
    /// </summary>
    public string? NextPageCursor { get; init; }
}
