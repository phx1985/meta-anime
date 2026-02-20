using MetaAnime.Domain.Entities;

namespace MetaAnime.Core.Interfaces.Repositories;

public interface IPropertyBagRepository
{
    Task<PropertyBag?> GetByKeyAsync(string key);
    Task<IEnumerable<PropertyBag>> GetAllAsync();
    Task UpsertAsync(string key, string value);
    Task DeleteAsync(string key);
}
