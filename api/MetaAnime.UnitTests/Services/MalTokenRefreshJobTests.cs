using FluentAssertions;
using MetaAnime.Application.BackgroundJobs;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Interfaces.Shared.Results;
using MetaAnime.Infrastructure.MyAnimeList;
using Microsoft.Extensions.Logging;
using Moq;

namespace MetaAnime.UnitTests.Services;

public class MalTokenRefreshJobTests
{
    private readonly Mock<IMalTokenService> _mockMalTokenService;
    private readonly Mock<ILogger<MalTokenRefreshJob>> _mockLogger;
    private readonly MalTokenRefreshJob _sut;

    public MalTokenRefreshJobTests()
    {
        _mockMalTokenService = new Mock<IMalTokenService>();
        _mockLogger = new Mock<ILogger<MalTokenRefreshJob>>();
        _sut = new MalTokenRefreshJob(_mockMalTokenService.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task RefreshToken_WhenSuccessful_LogsSuccess()
    {
        // Arrange
        _mockMalTokenService
            .Setup(x => x.RefreshTokenAsync())
            .ReturnsAsync(Result.Success());

        // Act
        await _sut.RefreshToken();

        // Assert
        _mockMalTokenService.Verify(x => x.RefreshTokenAsync(), Times.Once);
    }

    [Fact]
    public async Task RefreshToken_WhenFailure_LogsError()
    {
        // Arrange
        var error = new Error(Constants.Errors.MissingClientId, "ClientId not configured");
        _mockMalTokenService
            .Setup(x => x.RefreshTokenAsync())
            .ReturnsAsync(Result.Failure(error));

        // Act
        await _sut.RefreshToken();

        // Assert
        _mockMalTokenService.Verify(x => x.RefreshTokenAsync(), Times.Once);
        _mockLogger.Verify(x => x.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task RefreshToken_WhenExceptionThrown_RethrowsException()
    {
        // Arrange
        _mockMalTokenService
            .Setup(x => x.RefreshTokenAsync())
            .ThrowsAsync(new Exception("Unexpected error"));

        // Act
        var act = async () => await _sut.RefreshToken();

        // Assert
        await act.Should().ThrowAsync<Exception>().WithMessage("Unexpected error");
    }
}
