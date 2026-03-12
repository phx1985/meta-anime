namespace MetaAnime.Core.Models.BotSan;

public sealed class BotSanChatRequest
{
    public required string UserMessage { get; init; }
    public IList<ChatMessage> History { get; init; } = [];
}
