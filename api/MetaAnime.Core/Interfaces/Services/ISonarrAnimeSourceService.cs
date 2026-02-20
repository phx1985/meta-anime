using MetaAnime.Core.Interfaces.Mappers;
using MetaAnime.Core.Interfaces.Shared;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Core.Interfaces.Services;

public interface ISonarrAnimeSourceService<T> : IAnimeSourceService<T>
    where T : class, IAnimeConvertible, new()
{
    Task<Dictionary<string, int>> GetCodecDistributionAsync(string seriesId);
    /// <summary>
    /// Returns all tagged Sonarr series that have at least one episode file encoded with the
    /// specified codec, together with the per-codec episode-file counts and total size on disk.
    /// </summary>
    Task<Result<IList<SonarrSeriesCodecResult>>> GetSeriesByCodecAsync(string codec);
}

/// <summary>Light-weight projection returned by GetSeriesByCodecAsync.</summary>
public sealed record SonarrSeriesCodecResult(
    int Id,
    string Title,
    /// <summary>Episode-file counts broken down by video codec for this series.</summary>
    Dictionary<string, int> CodecDistribution,
    long TotalSizeOnDisk
);
