using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Core.Interfaces.Services;

public interface IKometaAnimeJsonService<T>
    where T : class
{
    Task<Result<IList<T>>> GetAllAsync();
}
