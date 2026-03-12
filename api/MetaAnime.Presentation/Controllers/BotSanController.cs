using Asp.Versioning;
using MetaAnime.Application.Services.BotSan;
using MetaAnime.Core.Models.BotSan;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace MetaAnime.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v1/bot-san")]
public sealed class BotSanController(IBotSanChatService botSanChatService) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Streams a Bot-san chat response as Server-Sent Events.
    /// Each event is <c>data: {"c":"token"}\n\n</c>; the final event is <c>data: [DONE]\n\n</c>.
    /// </summary>
    [HttpPost("chat")]
    [Authorize]
    public async Task StreamChat([FromBody] BotSanChatRequest request, CancellationToken cancellationToken)
    {
        Response.ContentType = "text/event-stream; charset=utf-8";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        try
        {
            await foreach (var chunk in botSanChatService.StreamChatAsync(request, cancellationToken))
            {
                var payload = JsonSerializer.Serialize(new { c = chunk }, JsonOptions);
                await Response.WriteAsync($"data: {payload}\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
            }

            await Response.WriteAsync("data: [DONE]\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Client disconnected — normal for SSE streams
        }
    }

    /// <summary>
    /// Streams a random Bot-san welcome greeting as Server-Sent Events.
    /// Same SSE format as /chat. Used on first open of the chat drawer.
    /// </summary>
    [HttpGet("greeting")]
    [Authorize]
    public async Task StreamGreeting(CancellationToken cancellationToken)
    {
        Response.ContentType = "text/event-stream; charset=utf-8";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        try
        {
            await foreach (var chunk in botSanChatService.StreamGreetingAsync(cancellationToken))
            {
                var payload = JsonSerializer.Serialize(new { c = chunk }, JsonOptions);
                await Response.WriteAsync($"data: {payload}\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
            }

            await Response.WriteAsync("data: [DONE]\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Client disconnected
        }
    }
}
