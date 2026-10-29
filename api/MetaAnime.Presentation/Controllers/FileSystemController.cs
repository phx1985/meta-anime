using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MetaAnime.Controllers;

/// <summary>
/// Server-side directory browser for the path-mapping settings UI.
/// Browsers never disclose absolute paths from native pickers, so the folder
/// picker navigates the API host's real filesystem instead — which is exactly
/// the machine whose mount paths are being configured.
/// Lists DIRECTORIES ONLY; never reads or returns file contents.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v1/fs")]
public sealed class FileSystemController : ControllerBase
{
    public sealed record BrowseDirectory(string Path, string? Parent, IReadOnlyList<BrowseEntry> Directories);
    public sealed record BrowseEntry(string Name, string Path);

    /// <summary>Lists the sub-directories of <paramref name="path"/> (default: filesystem root).</summary>
    [HttpGet("browse")]
    [Authorize]
    public ActionResult<BrowseDirectory> Browse([FromQuery] string? path)
    {
        string target;
        try
        {
            target = string.IsNullOrWhiteSpace(path)
                ? Path.GetPathRoot(Environment.CurrentDirectory) ?? "/"
                : Path.GetFullPath(path);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = $"Invalid path: {ex.Message}" });
        }

        if (!Directory.Exists(target))
            return NotFound(new { error = $"Directory not found: {target}" });

        try
        {
            var directories = Directory.EnumerateDirectories(target)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .Select(p => new BrowseEntry(Path.GetFileName(p), p))
                .ToList();

            var parent = Directory.GetParent(target)?.FullName;
            return Ok(new BrowseDirectory(target, parent, directories));
        }
        catch (UnauthorizedAccessException)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Permission denied" });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = ex.Message });
        }
    }
}
