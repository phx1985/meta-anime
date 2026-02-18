using System.Diagnostics;

namespace MetaAnime.Domain.Entities;

[DebuggerDisplay("[AniDbId: {AniDbId}] - {PlatformName}->[{PlatformId}]")]
public class KometaAnimeMapping
{
    public Guid Id { get; set; }
    public required string AniDbId { get; set; }
    public required string PlatformName { get; set; }
    public required string PlatformId { get; set; }
    public int? Season { get; set; }
    public int? EpOffset { get; set; }
}

