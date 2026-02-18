using MetaAnime.Core.Interfaces.Mappers;
using MetaAnime.Core.Interfaces.Shared;

namespace MetaAnime.Core.Interfaces.Services
{
    public interface ISonarrAnimeSourceService<T> : IAnimeSourceService<T>
        where T : class, IAnimeConvertible, new()
    {

    }
}
