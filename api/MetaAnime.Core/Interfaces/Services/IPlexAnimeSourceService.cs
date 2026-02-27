using MetaAnime.Core.Interfaces.Mappers;
using MetaAnime.Core.Interfaces.Shared;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Core.Interfaces.Services;

public interface IPlexAnimeSourceService<T> : IAnimeSourceService<T>
    where T : IAnimeConvertible
{
    Task<Result<string>> GetMachineIdentifierAsync();
}
