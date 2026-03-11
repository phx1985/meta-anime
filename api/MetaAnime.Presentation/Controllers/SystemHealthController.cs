using Asp.Versioning;
using MetaAnime.Core.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MetaAnime.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v1/system")]
public sealed class SystemHealthController(IProviderHealthCache healthCache) : ControllerBase
{
    /// <summary>
    /// Returns the latest cached provider health snapshot.
    /// Returns 503 if the background monitor has not run yet.
    /// </summary>
    [HttpGet("health")]
    [AllowAnonymous]
    public IActionResult GetHealth()
    {
        var snapshot = healthCache.Current;
        if (snapshot is null)
            return StatusCode(503, new { message = "Health monitor has not completed its first check yet." });

        return Ok(snapshot);
    }
}
