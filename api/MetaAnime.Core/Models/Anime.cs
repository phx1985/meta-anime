using MetaAnime.Core.Enums;

namespace MetaAnime.Core.Models;

public class Anime
{
    public Guid Id { get; set; }
    public required string SourceId { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public required AnimeSource AnimeSource { get; set; }
    public virtual ICollection<ExternalAnimeIdentifier> ExternalAnimeIdentifiers { get; set; } = [];
}


