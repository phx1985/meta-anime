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
        var expectedStats = new AnimeStatisticsResponse(500, 300, 400, 350, 280);
        _mockAnimeMediatorService
            .Setup(x => x.GetStatisticsAsync())
            .ReturnsAsync(Result.Success(expectedStats));

        // Act
        var result = await _sut.Handle(new GetAnimeStatisticsQuery(), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.TotalAnime.Should().Be(500);
        result.Value.PlexAnime.Should().Be(300);
        result.Value.MyAnimeListAnime.Should().Be(400);
        result.Value.SonarrAnime.Should().Be(350);
        result.Value.MatchedAnime.Should().Be(280);
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
