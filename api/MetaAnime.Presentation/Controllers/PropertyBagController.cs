using Asp.Versioning;
using Mediator;
using ApplicationConstants = MetaAnime.Application.Constants;
using MetaAnime.Application;
using MetaAnime.Application.Commands;
using MetaAnime.Application.Contracts;
using MetaAnime.Application.Queries;
using MetaAnime.Utilities.ErrorHandling;
using MetaAnime.Utilities.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MetaAnime.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v1/property-bag")]
public sealed class PropertyBagController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    [Authorize]
    public async Task<ActionResult<IEnumerable<PropertyBagResponse>>> GetAll()
    {
        var result = await mediator.Send(new GetAllPropertyBagQuery());
        if (result.IsFailure)
        {
            return this.ToErrorResult(result);
        }

        return Ok(result.Value);
    }

    [HttpGet("{key}")]
    [Authorize]
    public async Task<ActionResult<PropertyBagResponse>> GetByKey(string key)
    {
        // Internal System:* keys are excluded from the generic property-bag API —
        // the query handler reports them exactly like missing keys (404).
        var result = await mediator.Send(new GetPropertyBagByKeyQuery(key));
        if (result.IsFailure)
        {
            return this.ToErrorResult(result);
        }

        if (result.Value is null)
        {
            return NotFound();
        }

        return Ok(result.Value);
    }

    [HttpPut("{key}")]
    [Authorize]
    public async Task<ActionResult<PropertyBagResponse>> Upsert(string key, [FromBody] UpsertPropertyBagRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        // Internal System:* keys cannot be written through the generic
        // property-bag API at all (the settings API manages System:SessionLifetimeDays).
        if (PropertyBagKeyPolicy.IsSystemManaged(key))
        {
            return StatusCode(StatusCodes.Status400BadRequest,
                ProblemEnvelope.Create(StatusCodes.Status400BadRequest, $"Key '{key}' is managed by the system."));
        }

        var result = await mediator.Send(new UpsertPropertyBagCommand(key, request));
        if (result.IsFailure)
        {
            return this.ToErrorResult(result);
        }

        return Ok(result.Value);
    }

    [HttpDelete("{key}")]
    [Authorize]
    public async Task<IActionResult> Delete(string key)
    {
        // Internal System:* keys are excluded from the generic property-bag API —
        // the command handler reports them exactly like missing keys (404).
        var result = await mediator.Send(new DeletePropertyBagCommand(key));
        if (result.IsFailure)
        {
            return this.ToErrorResult(result);
        }

        return NoContent();
    }
}
