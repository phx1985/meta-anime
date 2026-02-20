using Asp.Versioning;
using MetaAnime.Application.Contracts;
using MetaAnime.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace MetaAnime.Controllers
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/sonarr")]
    public class SonarrController(
        IAnimeMediatorService animeMediatorService,
        ILogger<SonarrController> logger) : ControllerBase
    {
        /// <summary>
        /// Returns all Sonarr anime series that contain at least one episode file encoded with the
        /// specified video codec (e.g. "h264", "x264", "hevc", "av1").
        /// Useful for finding series that still need to be converted to a more efficient codec.
        /// The codec filter is case-insensitive and matches on partial name
        /// (e.g. "h264" matches both "h264" and "x264").
        /// </summary>
        [HttpGet("series")]
        [Authorize]
        public async Task<ActionResult<IEnumerable<SonarrCodecSearchResponse>>> GetSeriesByCodec(
            [FromQuery] string codec)
        {
            if (string.IsNullOrWhiteSpace(codec))
                return BadRequest("The 'codec' query parameter is required.");

            var result = await animeMediatorService.GetSonarrSeriesByCodecAsync(codec);

            if (result.IsFailure)
            {
                logger.LogError("Sonarr codec search failed for codec '{Codec}': {Error}", codec, result.Error.Message);
                return StatusCode(500, "An error occurred while searching Sonarr for series with the requested codec.");
            }

            return Ok(result.Value);
        }
    }
}
