using FluentAssertions;
using MetaAnime.Application.BackgroundJobs;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Interfaces.Shared.Results;
using MetaAnime.Core.Settings;
using MetaAnime.Infrastructure.MyAnimeList;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace MetaAnime.UnitTests.Services;

public class MalTokenRefreshJobTests
{
    private readonly Mock<IMalTokenService> _mockMalTokenService;
    private readonly Mock<ILogger<MalTokenRefreshJob>> _mockLogger;

    public MalTokenRefreshJobTests()
    {
        _mockMalTokenService = new Mock<IMalTokenService>();
        _mockLogger = new Mock<ILogger<MalTokenRefreshJob>>();
    }

    private MalTokenRefreshJob CreateSut(string? clientId = "test-client-id")
    {
        var malSettings = Options.Create(new MalSettings
        {
            ApiBaseUrl = "https://api.myanimelist.net/v2",
            ApiToken = "token",
            WebBaseUrl = "https://myanimelist.net",
            ClientId = clientId
        });
        return new MalTokenRefreshJob(_mockMalTokenService.Object, _mockLogger.Object, malSettings);
    }

    [Fact]
    public async Task RefreshToken_WhenClientIdIsNotConfigured_LogsWarningAndReturns()
    {
        // Arrange
        var sut = CreateSut(clientId: null);

        // Act
        await sut.RefreshToken();

        // Assert
        _mockMalTokenService.Verify(x => x.RefreshTokenAsync(), Times.Never);
        _mockLogger.Verify(x => x.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task RefreshToken_WhenClientIdIsEmpty_LogsWarningAndReturns()
    {
        // Arrange
        var sut = CreateSut(clientId: string.Empty);

        // Act
        await sut.RefreshToken();

        // Assert
        _mockMalTokenService.Verify(x => x.RefreshTokenAsync(), Times.Never);
        _mockLogger.Verify(x => x.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task RefreshToken_WhenSuccessful_LogsSuccess()
    {
        // Arrange
        var sut = CreateSut();
        _mockMalTokenService
            .Setup(x => x.RefreshTokenAsync())
            .ReturnsAsync(Result.Success());

        // Act
        await sut.RefreshToken();

        // Assert
        _mockMalTokenService.Verify(x => x.RefreshTokenAsync(), Times.Once);
    }

    [Fact]
    public async Task RefreshToken_WhenFailure_LogsError()
    {
        // Arrange
        var sut = CreateSut();
        var error = new Error(Constants.Errors.MissingClientId, "ClientId not configured");
        _mockMalTokenService
            .Setup(x => x.RefreshTokenAsync())
            .ReturnsAsync(Result.Failure(error));

        // Act
        await sut.RefreshToken();

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
        var sut = CreateSut();
        _mockMalTokenService
            .Setup(x => x.RefreshTokenAsync())
            .ThrowsAsync(new Exception("Unexpected error"));

        // Act
        var act = async () => await sut.RefreshToken();

        // Assert
        await act.Should().ThrowAsync<Exception>().WithMessage("Unexpected error");
    }
}
