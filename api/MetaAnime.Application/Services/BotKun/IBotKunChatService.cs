using MetaAnime.Core.Models.BotKun;

namespace MetaAnime.Application.Services.BotKun;

public interface IBotKunChatService
{
    IAsyncEnumerable<string> StreamChatAsync(BotKunChatRequest request, CancellationToken cancellationToken = default);
    IAsyncEnumerable<string> StreamGreetingAsync(CancellationToken cancellationToken = default);
    Task<string[]> GetAvailableModelsAsync(string baseUrl, string apiKey, CancellationToken cancellationToken = default);
}
