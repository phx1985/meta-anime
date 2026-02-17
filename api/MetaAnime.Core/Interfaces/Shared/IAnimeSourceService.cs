using MetaAnime.Core.Interfaces.Mappers;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Core.Interfaces.Shared
{
    /// <summary>
    /// This interface is used to define base methods to get anime from a source like AniList or MyAnimeList etc..   
    /// Methods: GetById, GetByTitle, GetAll
    /// </summary>
    public interface IAnimeSourceService<T>
        where T : IAnimeConvertible
    {
        /// <summary>
        /// Get anime by id
        /// </summary>
        /// <param name="id">Anime id</param>
        /// <returns>Return anime</returns>
        Task<Result<T>> GetByIdAsync(string id);
        /// <summary>
        /// Get all anime
        /// </summary>
        /// <returns>Return all anime</returns>
        Task<Result<IList<T>>> GetAllAsync();
    }
}
