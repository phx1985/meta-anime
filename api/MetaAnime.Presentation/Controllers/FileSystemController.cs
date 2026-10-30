using Asp.Versioning;
using MetaAnime.Utilities.ErrorHandling;
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
public sealed class FileSystemController(ILogger<FileSystemController> logger) : ControllerBase
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
            logger.LogWarning(ex, "Rejected invalid browse path");
            return BadRequest(ProblemEnvelope.Create(StatusCodes.Status400BadRequest, "Invalid path."));
        }

        if (!Directory.Exists(target))
            return NotFound(ProblemEnvelope.Create(StatusCodes.Status404NotFound, $"Directory not found: {target}"));

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
            return StatusCode(StatusCodes.Status403Forbidden, ProblemEnvelope.Create(StatusCodes.Status403Forbidden, "Permission denied"));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to enumerate directories for '{Target}'", target);
            return StatusCode(StatusCodes.Status500InternalServerError,
                ProblemEnvelope.Create(StatusCodes.Status500InternalServerError, "Failed to list directories."));
        }
    }
}
