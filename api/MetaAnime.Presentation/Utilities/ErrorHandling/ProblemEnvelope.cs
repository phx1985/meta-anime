using Microsoft.AspNetCore.Mvc;

namespace MetaAnime.Utilities.ErrorHandling;

/// <summary>
/// Unified error envelope for API error responses (RFC 9457 ProblemDetails fields
/// plus <c>message</c>/<c>error</c> kept as top-level members so every field the
/// Angular error readers use (<c>error.error?.message</c>, <c>error.error?.error</c>)
/// keeps working during the transition).
/// </summary>
public static class ProblemEnvelope
{
    public static ProblemDetails Create(int status, string message)
    {
        var problem = new ProblemDetails
        {
            Title = message,
            Status = status,
            Detail = message,
        };
        problem.Extensions["message"] = message;
        problem.Extensions["error"] = message;
        return problem;
    }
}
