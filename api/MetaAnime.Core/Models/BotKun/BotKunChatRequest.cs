namespace MetaAnime.Core.Models.BotKun;

public sealed class BotKunChatRequest
{
    public required string UserMessage { get; init; }
    public IList<ChatMessage> History { get; init; } = [];
    public string Language { get; init; } = "en";
}
