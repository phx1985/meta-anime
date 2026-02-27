using MetaAnime.Domain.Enums;

namespace MetaAnime.Domain.Entities;

public sealed class CacheEntry
{
    public Guid Id { get; set; }
    public required string SourceId { get; set; }
    public required string RawData { get; set; }
    public required AnimeSource AnimeSource { get; set; }
}


