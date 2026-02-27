using MetaAnime.Domain.Entities;

namespace MetaAnime.Core.Interfaces.Mappers;

public interface IAnimeConvertible
{
    /// <summary>
    /// Returns an instance of Anime
    /// </summary>
    /// <returns></returns>
    public Anime ToAnime();
}
