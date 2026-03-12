using MetaAnime.Core.Models.BotSan;

namespace MetaAnime.Application.Services.BotSan;

public interface IBotSanChatService
{
    IAsyncEnumerable<string> StreamChatAsync(BotSanChatRequest request, CancellationToken cancellationToken = default);
    IAsyncEnumerable<string> StreamGreetingAsync(CancellationToken cancellationToken = default);
    Task<string[]> GetAvailableModelsAsync(string baseUrl, string apiKey, CancellationToken cancellationToken = default);
}
