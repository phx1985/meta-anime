using MetaAnime.Domain.Entities;

namespace MetaAnime.Core.Interfaces.Repositories;

public interface IManualMediaMappingRepository
{
    Task<ManualMediaMapping?> GetByPlexSourceIdAsync(string plexSourceId);
    Task<IList<ManualMediaMapping>> GetAllAsync();
    Task AddAsync(ManualMediaMapping mapping);
    Task DeleteAsync(Guid id);
}
