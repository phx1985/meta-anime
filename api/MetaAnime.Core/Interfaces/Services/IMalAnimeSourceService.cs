using MetaAnime.Core.Interfaces.Mappers;
using MetaAnime.Core.Interfaces.Shared;

namespace MetaAnime.Core.Interfaces.Services;

public interface IMalAnimeSourceService<T> : IAnimeSourceService<T>
    where T : IAnimeConvertible
{
}
