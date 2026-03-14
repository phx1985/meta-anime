using Asp.Versioning;
using MetaAnime.Application.Services.BotKun;
using MetaAnime.Core.Models.BotKun;
using MetaAnime.Presentation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
namespace MetaAnime.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v1/bot-kun")]
public sealed class BotKunController(IBotKunChatService botKunChatService) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Streams a Bot-kun chat response as Server-Sent Events.
    /// Each event is <c>data: {"c":"token"}\n\n</c>; the final event is <c>data: [DONE]\n\n</c>.
    /// </summary>
    [HttpPost("chat")]
    [Authorize]
    public async Task StreamChat([FromBody] BotKunChatRequest request, CancellationToken cancellationToken)
    {
        Response.ContentType = SseConstants.ContentType;
        Response.Headers["Cache-Control"] = SseConstants.CacheControl;
        Response.Headers["X-Accel-Buffering"] = SseConstants.XAccelBuffering;

        try
        {
            await foreach (var chunk in botKunChatService.StreamChatAsync(request, cancellationToken))
            {
                var payload = JsonSerializer.Serialize(new { c = chunk }, JsonOptions);
                await Response.WriteAsync($"{SseConstants.DataPrefix}{payload}\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
            }

            await Response.WriteAsync(SseConstants.DoneMessage, cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Client disconnected — normal for SSE streams
        }
    }

    /// <summary>
    /// Streams a random Bot-kun welcome greeting as Server-Sent Events.
    /// Same SSE format as /chat. Used on first open of the chat drawer.
    /// </summary>
    [HttpGet("greeting")]
    [Authorize]
    public async Task StreamGreeting(CancellationToken cancellationToken)
    {
        Response.ContentType = SseConstants.ContentType;
        Response.Headers["Cache-Control"] = SseConstants.CacheControl;
        Response.Headers["X-Accel-Buffering"] = SseConstants.XAccelBuffering;

        try
        {
            await foreach (var chunk in botKunChatService.StreamGreetingAsync(cancellationToken))
            {
                var payload = JsonSerializer.Serialize(new { c = chunk }, JsonOptions);
                await Response.WriteAsync($"{SseConstants.DataPrefix}{payload}\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
            }

            await Response.WriteAsync(SseConstants.DoneMessage, cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Client disconnected
        }
    }

    /// <summary>
    /// Fetches the list of available model IDs from the configured AI provider.
    /// </summary>
    [HttpPost("models")]
    [Authorize]
    public async Task<ActionResult<string[]>> GetModels(
        [FromBody] BotKunModelsRequest request,
        CancellationToken cancellationToken)
    {
        var models = await botKunChatService.GetAvailableModelsAsync(
            request.BaseUrl, request.ApiKey, cancellationToken);
        return Ok(models);
    }
}
