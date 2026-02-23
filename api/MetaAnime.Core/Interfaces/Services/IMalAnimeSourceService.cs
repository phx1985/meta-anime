using MetaAnime.Core.Interfaces.Mappers;
using MetaAnime.Core.Interfaces.Shared;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Core.Interfaces.Services;

public interface IMalAnimeSourceService<T> : IAnimeSourceService<T>
    where T : IAnimeConvertible
{
    Task<Result<(IList<T> Items, bool HasNextPage)>> GetSeasonalAsync(int year, string season, int offset, int limit);
    Task<Result> UpdateListStatusAsync(string animeId, string status, int numWatchedEpisodes, int score);
}
