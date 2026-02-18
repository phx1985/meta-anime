namespace MetaAnime.Application.DTOs
{
    public sealed record UpdateAnimeRequest(
        string Title,
        string? Description
    );
}
