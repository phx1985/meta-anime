using MetaAnime.Core.Logging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MetaAnime.Presentation.Controllers;

/// <summary>Recent in-process log lines, for the Settings → Sync "server logs" panel.</summary>
[ApiController]
[Route("api/v1/logs")]
[Authorize]
public sealed class LogsController(InMemoryLogStore store) : ControllerBase
{
    public sealed record LogsResponse(IReadOnlyList<LogEntry> Items, int Capacity);

    [HttpGet]
    public ActionResult<LogsResponse> Get(
        [FromQuery] int take = 200,
        [FromQuery] string? level = null,
        [FromQuery] string? search = null) =>
        Ok(new LogsResponse(store.Snapshot(take, level, search), InMemoryLogStore.Capacity));

    [HttpDelete]
    public IActionResult Clear()
    {
        store.Clear();
        return NoContent();
    }
}
