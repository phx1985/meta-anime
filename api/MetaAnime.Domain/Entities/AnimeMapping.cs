namespace MetaAnime.Domain.Entities;

public sealed class AnimeMapping
{
    public Guid Id { get; set; }
    public required string AniDbId { get; set; }
    public Guid AnimeId { get; set; }
    public Anime? Anime { get; set; }
}


