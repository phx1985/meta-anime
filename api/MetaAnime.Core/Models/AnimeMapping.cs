namespace MetaAnime.Core.Models;

public class AnimeMapping
{
    public Guid Id { get; set; }
    public required string AniDbId { get; set; }
    public Guid AnimeId { get; set; }
    public virtual Anime? Anime { get; set; }
}


