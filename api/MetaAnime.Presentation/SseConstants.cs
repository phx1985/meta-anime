namespace MetaAnime.Presentation;

/// <summary>
/// Shared Server-Sent Events (SSE) format strings used by streaming controllers.
/// </summary>
public static class SseConstants
{
    public const string ContentType      = "text/event-stream; charset=utf-8";
    public const string CacheControl     = "no-cache";
    public const string XAccelBuffering  = "no";
    public const string DoneMessage      = "data: [DONE]\n\n";
    public const string DataPrefix       = "data: ";
}
