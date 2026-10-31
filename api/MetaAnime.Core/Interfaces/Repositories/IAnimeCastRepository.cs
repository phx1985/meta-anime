using MetaAnime.Domain.Entities;

namespace MetaAnime.Core.Interfaces.Repositories;

public interface IAnimeCastRepository
{
    Task<IEnumerable<AnimeCast>> GetByAnimeMalIdAsync(string animeMalId);
    /// <summary>Returns all distinct anime MAL IDs that appear as VA roles for a given person.</summary>
    Task<IEnumerable<AnimeCast>> GetByVaPersonIdAsync(int vaPersonId);
    Task BulkUpsertAsync(IEnumerable<AnimeCast> entries);
    Task DeleteByAnimeMalIdAsync(string animeMalId);
    /// <summary>Deletes every cast row (one-time ID-space migration).</summary>
    Task DeleteAllAsync();
}
