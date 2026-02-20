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
    [Route("api/v{version:apiVersion}/plex")]
    public class PlexController(
        IAnimeMediatorService animeMediatorService,
        ILogger<PlexController> logger) : ControllerBase
    {
        /// <summary>
        /// Returns all available Plex client devices (players) that are currently online
        /// and can receive navigation commands.
        /// </summary>
        [HttpGet("devices")]
        [Authorize]
        public async Task<ActionResult<IEnumerable<PlexClientDeviceResponse>>> GetDevices()
        {
            var result = await animeMediatorService.GetPlexDevicesAsync();

            if (result.IsFailure)
            {
                logger.LogError("Failed to retrieve Plex devices: {Error}", result.Error.Message);
                return StatusCode(500, "An error occurred while retrieving Plex devices.");
            }

            return Ok(result.Value);
        }

        /// <summary>
        /// Opens the series lobby page for the anime identified by the given AniDB ID
        /// on the specified Plex client device.
        /// </summary>
        /// <param name="aniDbId">The AniDB ID of the anime series</param>
        /// <param name="clientIdentifier">The client identifier of the target Plex device (from GET /plex/devices)</param>
        [HttpPost("{aniDbId}/navigate/{clientIdentifier}")]
        [Authorize]
        public async Task<IActionResult> NavigateToSeries(string aniDbId, string clientIdentifier)
        {
            var result = await animeMediatorService.NavigateToPlexSeriesAsync(aniDbId, clientIdentifier);

            if (result.IsFailure)
            {
                if (result.Error.Code == "Plex.SeriesNotFound")
                    return NotFound(result.Error.Message);

                logger.LogError("Failed to navigate Plex device '{ClientIdentifier}' to series '{AniDbId}': {Error}",
                    clientIdentifier, aniDbId, result.Error.Message);
                return StatusCode(500, "An error occurred while sending the navigation command.");
            }

            return Ok(new { message = "Navigation command sent successfully." });
        }
    }
}
