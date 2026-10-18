using Asp.Versioning;
using MetaAnime.Core.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MetaAnime.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v1/cast")]
public sealed class CastController(
    ICastService castService,
    ILogger<CastController> logger) : ControllerBase
{
    /// <summary>Discovers cast-capable devices on the local network.</summary>
    [HttpGet("devices")]
    [Authorize]
    public async Task<ActionResult<IEnumerable<CastDeviceResponse>>> GetDevices()
    {
        var result = await castService.GetDevicesAsync();
        if (result.IsFailure)
        {
            logger.LogError("Cast device discovery failed: {Error}", result.Error.Message);
            return StatusCode(500, result.Error.Message);
        }

        return Ok(result.Value.Select(d =>
            new CastDeviceResponse(d.Id, d.Name, d.Type, d.Host, d.Port, d.Model)));
    }

    /// <summary>Casts the next episode of a show to the selected device.</summary>
    [HttpPost("play")]
    [Authorize]
    public async Task<ActionResult<CastPlayResponse>> Play(CastPlayRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.DeviceHost))
            return BadRequest("deviceHost is required.");
        if (string.IsNullOrWhiteSpace(request.ShowRatingKey))
            return BadRequest("showRatingKey is required.");

        var result = await castService.CastShowAsync(
            request.DeviceHost, request.ShowRatingKey, request.OffsetMs);

        if (result.IsFailure)
        {
            logger.LogError("Cast failed for show {Show} -> {Device}: {Error}",
                request.ShowRatingKey, request.DeviceHost, result.Error.Message);
            return StatusCode(500, result.Error.Message);
        }

        logger.LogInformation("Cast started: '{Title}' -> {Device}",
            result.Value.Title, result.Value.DeviceName);

        return Ok(new CastPlayResponse(
            result.Value.DeviceName, result.Value.Title, result.Value.SubTitle, result.Value.StreamUrl));
    }
}

public sealed record CastPlayRequest(string DeviceHost, string ShowRatingKey, long OffsetMs = 0);

public sealed record CastDeviceResponse(
    string Id, string Name, string Type, string Host, int Port, string? Model);

public sealed record CastPlayResponse(
    string DeviceName, string Title, string SubTitle, string StreamUrl);
