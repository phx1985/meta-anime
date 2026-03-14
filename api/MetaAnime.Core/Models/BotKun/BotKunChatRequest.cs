namespace MetaAnime.Core.Models.BotKun;

public sealed class BotKunChatRequest
{
    public required string UserMessage { get; init; }
    public IList<ChatMessage> History { get; init; } = [];
    public string Language { get; init; } = "en";

    // R22: explicit pagination context so the service can reliably page through
    // the same query without relying solely on the LLM's page extraction.
    // The client sends the last page it received; the service adds 1 when appropriate.
    public int? PreviousSearchPage { get; init; }
    public string? PreviousSearchQuery { get; init; }
}
