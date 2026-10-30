using MetaAnime.Utilities.ErrorHandling;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace MetaAnime.Utilities.ErrorHandling;

/// <summary>
/// Single IExceptionHandler (RFC 9457) for unhandled exceptions: logs the full
/// exception server-side and answers with the unified ProblemDetails envelope and
/// a generic message — raw exception details are never leaked to clients.
/// </summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new(System.Text.Json.JsonSerializerDefaults.Web);

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        logger.LogError(exception, "Unhandled exception handling {Method} {Path}",
            httpContext.Request.Method, httpContext.Request.Path);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await httpContext.Response.WriteAsJsonAsync(
            ProblemEnvelope.Create(StatusCodes.Status500InternalServerError, "An unexpected error occurred."),
            JsonOptions,
            cancellationToken);
        return true;
    }
}
