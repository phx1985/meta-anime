using FluentAssertions;
using MetaAnime.Application.Contracts;
using MetaAnime.Application.Interfaces;
using MetaAnime.Application.Queries;
using MetaAnime.Core.Interfaces.Shared.Results;
using Moq;

namespace MetaAnime.UnitTests.Services;

public class GetAnimeStatisticsQueryHandlerTests
{
    private readonly Mock<IAnimeMediatorService> _mockAnimeMediatorService;
    private readonly GetAnimeStatisticsQueryHandler _sut;

    public GetAnimeStatisticsQueryHandlerTests()
    {
        _mockAnimeMediatorService = new Mock<IAnimeMediatorService>();
        _sut = new GetAnimeStatisticsQueryHandler(_mockAnimeMediatorService.Object);
    }

    [Fact]
    public async Task Handle_WhenServiceSucceeds_ReturnsStatistics()
    {
        // Arrange
        var genreStats = new Dictionary<string, int> { { "Action", 10 }, { "Drama", 5 } };
        var viewingProgress = new ViewingProgressStats(120, 300, 80, 200);
        var expectedStats = new AnimeStatisticsResponse(300, 400, 350, 280, genreStats, viewingProgress);
        _mockAnimeMediatorService
            .Setup(x => x.GetStatisticsAsync())
            .ReturnsAsync(Result.Success(expectedStats));

        // Act
        var result = await _sut.Handle(new GetAnimeStatisticsQuery(), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.PlexAnime.Should().Be(300);
        result.Value.MyAnimeListAnime.Should().Be(400);
        result.Value.SonarrAnime.Should().Be(350);
        result.Value.MatchedAnime.Should().Be(280);
        result.Value.GenreStats.Should().ContainKey("Action").WhoseValue.Should().Be(10);
        result.Value.GenreStats.Should().ContainKey("Drama").WhoseValue.Should().Be(5);
        result.Value.ViewingProgress.MalEpisodesWatched.Should().Be(120);
        result.Value.ViewingProgress.MalTotalEpisodes.Should().Be(300);
        result.Value.ViewingProgress.PlexEpisodesWatched.Should().Be(80);
        result.Value.ViewingProgress.PlexTotalEpisodes.Should().Be(200);
    }

    [Fact]
    public async Task Handle_WhenServiceFails_ReturnsFailureResult()
    {
        // Arrange
        var error = new Error("Error.GetStatisticsAsync", "An error occurred");
        _mockAnimeMediatorService
            .Setup(x => x.GetStatisticsAsync())
            .ReturnsAsync(Result.Failure<AnimeStatisticsResponse>(error));

        // Act
        var result = await _sut.Handle(new GetAnimeStatisticsQuery(), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Error.GetStatisticsAsync");
    }
}
