using MetaAnime.Core.Models;

namespace MetaAnime.Core.Interfaces.Services;

/// <summary>
/// Manages user-confirmed AniDbId cross-links stored as a single JSON array
/// in the PropertyBag table. Cross-links survive sync because PropertyBag
/// is not FK-linked to Anime rows.
/// </summary>
public interface ICrossLinkService
{
    Task<List<CrossLinkEntry>> GetAllAsync();
    Task AddAsync(string sourceAniDbId, string malId);
    Task RemoveAsync(string sourceAniDbId, string malId);
}
