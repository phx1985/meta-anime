namespace MetaAnime.Application.Contracts
{
    public sealed record UpdateAnimeRequest(
        string Title,
        string? Description
    );
}
