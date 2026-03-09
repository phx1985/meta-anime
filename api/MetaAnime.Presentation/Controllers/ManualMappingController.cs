using Asp.Versioning;
using MetaAnime.Application.Contracts;
using MetaAnime.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace MetaAnime.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v1/manual-mappings")]
[Authorize]
public sealed class ManualMappingController(
    IAnimeMediatorService animeMediatorService,
    ILogger<ManualMappingController> logger) : ControllerBase
{
    /// <summary>Returns all manual media mappings.</summary>
    [HttpGet]
    public async Task<ActionResult> GetAll()
    {
        var result = await animeMediatorService.GetAllManualMappingsAsync();
        if (result.IsFailure)
        {
            logger.LogError("Failed to fetch manual mappings: {Error}", result.Error.Message);
            return StatusCode(500, result.Error.Message);
        }
        return Ok(result.Value);
    }

    /// <summary>Creates a new manual Plex→MAL mapping and adds the anime to the user's MAL list.</summary>
    [HttpPost]
    public async Task<ActionResult> Create([FromBody] CreateManualMappingRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.PlexSourceId) || string.IsNullOrWhiteSpace(request.MalId))
            return BadRequest("PlexSourceId and MalId are required.");

        var result = await animeMediatorService.CreateManualMappingAsync(request);
        if (result.IsFailure)
        {
            logger.LogError("Failed to create manual mapping for PlexSourceId '{PlexId}': {Error}",
                request.PlexSourceId, result.Error.Message);
            return StatusCode(500, result.Error.Message);
        }
        return CreatedAtAction(nameof(GetAll), result.Value);
    }

    /// <summary>Deletes a manual mapping by ID.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> Delete(Guid id)
    {
        var result = await animeMediatorService.DeleteManualMappingAsync(id);
        if (result.IsFailure)
        {
            logger.LogError("Failed to delete manual mapping '{Id}': {Error}", id, result.Error.Message);
            return StatusCode(500, result.Error.Message);
        }
        return NoContent();
    }
}
