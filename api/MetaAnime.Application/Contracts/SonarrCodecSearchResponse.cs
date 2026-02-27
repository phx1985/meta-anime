namespace MetaAnime.Application.Contracts;

/// <summary>
/// A Sonarr series that contains at least one episode file encoded with the requested codec.
/// </summary>
public sealed record SonarrCodecSearchResponse(
    int Id,
    string Title,
    string Url,
    /// <summary>Episode-file counts broken down by video codec for this series.</summary>
    Dictionary<string, int> CodecDistribution,
    /// <summary>Total size on disk of all episode files for this series (bytes).</summary>
    long TotalSizeOnDisk
);
