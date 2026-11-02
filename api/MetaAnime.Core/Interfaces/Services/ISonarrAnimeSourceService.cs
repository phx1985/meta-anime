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

    /// <summary>Creates a new tag in the Sonarr instance and returns the created tag.</summary>
    Task<Result<SonarrTagInfo>> CreateTagAsync(string label);

    /// <summary>
    /// Returns the "download folder imported" events Sonarr recorded strictly after
    /// <paramref name="sinceUtc"/>, with the series title and episode numbers needed by the
    /// "what was added" dialog. One unsorted call to <c>api/v3/history/since</c> — cheap
    /// enough to run on every cache cycle, unlike paging the sorted history table.
    /// </summary>
    Task<Result<IList<SonarrDownloadEvent>>> GetDownloadHistorySinceAsync(DateTime sinceUtc);
}

/// <summary>Lightweight projection of a Sonarr history download event.</summary>
/// <param name="Id">Sonarr's history record id (unique per instance; merge key).</param>
/// <param name="Date">UTC import timestamp.</param>
/// <param name="SizeBytes">Imported file size in bytes.</param>
/// <param name="SeriesId">Sonarr series id.</param>
/// <param name="EpisodeId">Sonarr episode id.</param>
/// <param name="SeriesTitle">Series title (empty when Sonarr could not resolve it).</param>
/// <param name="SeasonNumber">Season number (0 = specials).</param>
/// <param name="EpisodeNumber">Episode number within the season.</param>
public sealed record SonarrDownloadEvent(
    long Id,
    DateTime Date,
    long SizeBytes,
    int SeriesId = 0,
    int EpisodeId = 0,
    string SeriesTitle = "",
    int SeasonNumber = 0,
    int EpisodeNumber = 0);

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
/// <param name="SonarrSeriesId">The Sonarr internal series Id (> 0 when already in library).</param>
/// <param name="AnimeDbId">The MetaAnime DB GUID of the matching Anime entity; null when not yet persisted.</param>
/// <param name="MalId">The MyAnimeList anime ID resolved via Kometa (TvdbId → AniDbId → MalId); null when not found.</param>
public sealed record SonarrLookupItem(int TvdbId, string Title, int? Year, string? TitleSlug, string? RemotePoster, string? Overview, bool AlreadyInLibrary = false, int SonarrSeriesId = 0, Guid? AnimeDbId = null, string? MalId = null);

/// <summary>A tag defined in the Sonarr instance.</summary>
public sealed record SonarrTagInfo(int Id, string Label);
