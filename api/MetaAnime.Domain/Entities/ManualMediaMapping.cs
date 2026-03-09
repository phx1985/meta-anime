namespace MetaAnime.Domain.Entities;

public sealed class ManualMediaMapping
{
    public Guid Id { get; set; }
    public required string PlexSourceId { get; set; }
    public required string PlexTitle { get; set; }
    public required string MalId { get; set; }
    public required string MalTitle { get; set; }
    public required string AniDbId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
