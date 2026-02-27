using MetaAnime.Core.Interfaces.Mappers;
using MetaAnime.Core.Interfaces.Shared;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Core.Interfaces.Services;

public interface IMalAnimeSourceService<T> : IAnimeSourceService<T>
    where T : IAnimeConvertible
{
    Task<Result<IList<T>>> GetSeasonalAsync(int year, string season);
    Task<Result> UpdateListStatusAsync(string animeId, string status, int numWatchedEpisodes, int score);
}
