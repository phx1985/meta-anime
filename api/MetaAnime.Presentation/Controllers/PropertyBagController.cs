using Asp.Versioning;
using Mediator;
using ApplicationConstants = MetaAnime.Application.Constants;
using MetaAnime.Application.Commands;
using MetaAnime.Application.Contracts;
using MetaAnime.Application.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MetaAnime.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/property-bag")]
public sealed class PropertyBagController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    [Authorize]
    public async Task<ActionResult<IEnumerable<PropertyBagResponse>>> GetAll()
    {
        var result = await mediator.Send(new GetAllPropertyBagQuery());

        if (result.IsFailure)
        {
            return StatusCode(500, result.Error.Message);
        }

        return Ok(result.Value);
    }

    [HttpGet("{key}")]
    [Authorize]
    public async Task<ActionResult<PropertyBagResponse>> GetByKey(string key)
    {
        var result = await mediator.Send(new GetPropertyBagByKeyQuery(key));

        if (result.IsFailure)
        {
            return StatusCode(500, result.Error.Message);
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

        var result = await mediator.Send(new UpsertPropertyBagCommand(key, request));

        if (result.IsFailure)
        {
            return StatusCode(500, result.Error.Message);
        }

        return Ok(result.Value);
    }

    [HttpDelete("{key}")]
    [Authorize]
    public async Task<IActionResult> Delete(string key)
    {
        var result = await mediator.Send(new DeletePropertyBagCommand(key));

        if (result.IsFailure)
        {
            if (result.Error.Code == ApplicationConstants.Errors.NotFound)
            {
                return NotFound(result.Error.Message);
            }
            return StatusCode(500, result.Error.Message);
        }

        return NoContent();
    }
}
