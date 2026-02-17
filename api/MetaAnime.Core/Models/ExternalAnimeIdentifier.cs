using MetaAnime.Core.Enums;

namespace MetaAnime.Core.Models;

public class ExternalAnimeIdentifier
{
    public Guid Id { get; set; }
    public Guid AnimeId { get; set; }
    public virtual Anime? Anime { get; set; }
    public MediaPlatform MediaPlatform { get; set; }
    public required string MediaPlatformId { get; set; }
}


