using MetaAnime.Core.Interfaces.Mappers;
using MetaAnime.Core.Interfaces.Shared;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Core.Interfaces.Services;

public interface ISonarrAnimeSourceService<T> : IAnimeSourceService<T>
    where T : class, IAnimeConvertible, new()
{
    Task<Dictionary<string, int>> GetCodecDistributionAsync(string seriesId);
    /// <summary>
    /// Fetches codec distributions for multiple series in parallel using a single settings
    /// lookup, avoiding concurrent DbContext access.
    /// Returns a map of seriesId → per-codec episode-file counts.
    /// </summary>
    Task<Dictionary<string, Dictionary<string, int>>> GetBulkCodecDistributionsAsync(IEnumerable<string> seriesIds);
    /// <summary>
    /// Returns all tagged Sonarr series that have at least one episode file encoded with the
    /// specified codec, together with the per-codec episode-file counts and total size on disk.
    /// </summary>
    Task<Result<IList<SonarrSeriesCodecResult>>> GetSeriesByCodecAsync(string codec);

    /// <summary>Returns root folders, quality profiles and configured anime tag IDs.</summary>
    Task<Result<SonarrInstanceOptions>> GetOptionsAsync();

    /// <summary>Searches Sonarr for a series by TVDB ID and adds it with the supplied options.</summary>
    Task<Result<SonarrAddedSeries>> AddSeriesAsync(int tvdbId, string rootFolderPath, int qualityProfileId, IList<int> tagIds, string monitor = "all", bool seasonFolder = true);

    /// <summary>Searches Sonarr's series lookup API by free-text term (title).</summary>
    Task<Result<IList<SonarrLookupItem>>> SearchLookupAsync(string term);

    /// <summary>Removes a Sonarr series by its Sonarr series ID (does not delete episode files).</summary>
    Task<Result> RemoveSeriesAsync(int sonarrId);

    /// <summary>Returns all tags defined in the Sonarr instance with their ID and label.</summary>
    Task<Result<IList<SonarrTagInfo>>> GetAllTagsAsync();
}

/// <summary>Light-weight projection returned by GetSeriesByCodecAsync.</summary>
public sealed record SonarrSeriesCodecResult(
    int Id,
    string Title,
    /// <summary>Episode-file counts broken down by video codec for this series.</summary>
    Dictionary<string, int> CodecDistribution,
    long TotalSizeOnDisk
);

/// <summary>Available Sonarr configuration options needed when adding a series.</summary>
public sealed record SonarrInstanceOptions(
    IList<SonarrRootFolderInfo> RootFolders,
    IList<SonarrQualityProfileInfo> QualityProfiles,
    IList<int> AnimeTagIds
);

public sealed record SonarrRootFolderInfo(int Id, string Path, long FreeSpace);
public sealed record SonarrQualityProfileInfo(int Id, string Name);

/// <summary>Minimal projection of a Sonarr series returned after successfully adding it.</summary>
public sealed record SonarrAddedSeries(int Id, string Title, string TitleSlug);

/// <summary>Single result from the Sonarr series lookup API.</summary>
/// <param name="AlreadyInLibrary">True when Sonarr already tracks this series (Id > 0).</param>
public sealed record SonarrLookupItem(int TvdbId, string Title, int? Year, string? TitleSlug, string? RemotePoster, string? Overview, bool AlreadyInLibrary = false);

/// <summary>A tag defined in the Sonarr instance.</summary>
public sealed record SonarrTagInfo(int Id, string Label);
