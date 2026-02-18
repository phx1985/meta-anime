using MetaAnime.Domain.Entities;

namespace MetaAnime.Core.Interfaces.Caching
{
    public interface ICacheable
    {
        /// <summary>
        /// Returns an instance of CacheEntry
        /// </summary>
        /// <returns></returns>
        public CacheEntry ToCacheEntry();
    }
}
