namespace MetaAnime.Application.Contracts
{
    public sealed record AnimeStatisticsResponse(
        int TotalAnime,
        int PlexAnime,
        int MyAnimeListAnime,
        int SonarrAnime,
        int MatchedAnime
    );
}
