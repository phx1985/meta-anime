using MetaAnime.Core.Interfaces.Shared.Results;
using MetaAnime.Utilities.ErrorHandling;
using Microsoft.AspNetCore.Mvc;

namespace MetaAnime.Utilities.Extensions;

/// <summary>
/// The single Result→error mapping used by all controllers. Status codes are
/// derived from the error code so existing API status semantics are preserved
/// (NotFound→404, invalid search query→400, auth failures→401/400/409, the
/// rest→500) and the body is the unified ProblemDetails envelope.
/// </summary>
public static class ControllerResultExtensions
{
    public static ObjectResult ToErrorResult(
        this ControllerBase controller,
        Result result,
        int? statusCode = null,
        string? message = null)
    {
        var text = message ?? result.Error?.Message ?? "An unexpected error occurred.";
        var status = statusCode ?? MapStatus(result.Error?.Code);
        return new ObjectResult(ProblemEnvelope.Create(status, text)) { StatusCode = status };
    }

    private static int MapStatus(string? code) => code switch
    {
        MetaAnime.Application.Constants.Errors.NotFound => StatusCodes.Status404NotFound,
        MetaAnime.Application.Constants.Errors.AuthNotConfigured => StatusCodes.Status401Unauthorized,
        MetaAnime.Application.Constants.Errors.AuthInvalidPassword => StatusCodes.Status401Unauthorized,
        MetaAnime.Application.Constants.Errors.AuthPasswordAlreadySet => StatusCodes.Status409Conflict,
        MetaAnime.Application.Constants.Errors.AuthEmptyPassword => StatusCodes.Status400BadRequest,
        MetaAnime.Application.Constants.Errors.AuthNotEnabled => StatusCodes.Status400BadRequest,
        MetaAnime.Application.Constants.Errors.PropertyBagKeyNotAllowed => StatusCodes.Status400BadRequest,
        MetaAnime.Application.Constants.Errors.InvalidBotKunProviderUrl => StatusCodes.Status400BadRequest,
        MetaAnime.Infrastructure.MyAnimeList.Constants.Errors.InvalidSearchQuery => StatusCodes.Status400BadRequest,
        _ => StatusCodes.Status500InternalServerError,
    };
}
