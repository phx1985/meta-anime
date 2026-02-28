using FluentAssertions;
using MetaAnime.Core;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Domain.Entities;
using MetaAnime.Infrastructure.MyAnimeList.Models;
using MetaAnime.Infrastructure.MyAnimeList.Services;
using Microsoft.Extensions.Logging;
using Moq;
using MalConstants = MetaAnime.Infrastructure.MyAnimeList.Constants;

namespace MetaAnime.UnitTests.Services;

public class MalAnimeSourceServiceTests
{
    private readonly Mock<IPropertyBagRepository> _mockRepo;
    private readonly Mock<IMalTokenService> _mockTokenService;
    private readonly MalAnimeSourceService _sut;

    public MalAnimeSourceServiceTests()
    {
        _mockRepo = new Mock<IPropertyBagRepository>();
        _mockTokenService = new Mock<IMalTokenService>();

        _sut = new MalAnimeSourceService(
            _mockRepo.Object,
            _mockTokenService.Object,
            Mock.Of<ILogger<MalAnimeSourceService>>());
    }

    [Fact]
    public async Task GetAllAsync_WhenApiBaseUrlIsBlankInDatabase_ReturnsFailure()
    {
        // Arrange: PropertyBag has the key but value is blank
        _mockRepo
            .Setup(r => r.GetByKeyAsync(SettingsKeys.MalApiBaseUrl))
            .ReturnsAsync(new PropertyBag { Key = SettingsKeys.MalApiBaseUrl, Value = "   " });

        // Act
        var result = await _sut.GetAllAsync();

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MalConstants.Errors.NotConfigured);
        result.Error.Message.Should().Contain(SettingsKeys.MalApiBaseUrl);
    }

    [Fact]
    public async Task GetAllAsync_WhenApiBaseUrlIsNullInDatabase_ReturnsFailure()
    {
        // Arrange: no record at all in the PropertyBag
        _mockRepo
            .Setup(r => r.GetByKeyAsync(SettingsKeys.MalApiBaseUrl))
            .ReturnsAsync((PropertyBag?)null);

        // Act
        var result = await _sut.GetAllAsync();

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MalConstants.Errors.NotConfigured);
    }

    [Fact]
    public async Task GetAllAsync_WhenApiBaseUrlIsConfiguredButNoToken_ReturnsFailure()
    {
        // Arrange
        _mockRepo
            .Setup(r => r.GetByKeyAsync(SettingsKeys.MalApiBaseUrl))
            .ReturnsAsync(new PropertyBag { Key = SettingsKeys.MalApiBaseUrl, Value = "https://api.myanimelist.net/v2" });

        _mockTokenService
            .Setup(t => t.GetAccessTokenAsync())
            .ReturnsAsync((string?)null);

        // Act
        var result = await _sut.GetAllAsync();

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MalConstants.Errors.NoToken);
    }

    [Fact]
    public async Task GetAllAsync_ReadsBaseUrlFromDatabaseOnEachCall()
    {
        // Arrange: first call returns blank (no token needed — fails on URL check)
        _mockRepo
            .SetupSequence(r => r.GetByKeyAsync(SettingsKeys.MalApiBaseUrl))
            .ReturnsAsync((PropertyBag?)null)        // first call → not configured
            .ReturnsAsync(new PropertyBag { Key = SettingsKeys.MalApiBaseUrl, Value = "   " }); // second call → still blank

        // Act
        var firstResult = await _sut.GetAllAsync();
        var secondResult = await _sut.GetAllAsync();

        // Assert: both calls hit the database (repository called twice)
        _mockRepo.Verify(r => r.GetByKeyAsync(SettingsKeys.MalApiBaseUrl), Times.Exactly(2));
        firstResult.IsFailure.Should().BeTrue();
        secondResult.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task GetByIdAsync_WhenApiBaseUrlIsBlank_ReturnsFailure()
    {
        // Arrange
        _mockRepo
            .Setup(r => r.GetByKeyAsync(SettingsKeys.MalApiBaseUrl))
            .ReturnsAsync((PropertyBag?)null);

        // Act
        var result = await _sut.GetByIdAsync("12345");

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MalConstants.Errors.NotConfigured);
    }

    [Fact]
    public async Task GetSeasonalAsync_WhenApiBaseUrlIsBlank_ReturnsFailure()
    {
        // Arrange
        _mockRepo
            .Setup(r => r.GetByKeyAsync(SettingsKeys.MalApiBaseUrl))
            .ReturnsAsync(new PropertyBag { Key = SettingsKeys.MalApiBaseUrl, Value = "" });

        // Act
        var result = await _sut.GetSeasonalAsync(2025, "winter");

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MalConstants.Errors.NotConfigured);
    }

    [Fact]
    public async Task UpdateListStatusAsync_WhenApiBaseUrlIsBlank_ReturnsFailure()
    {
        // Arrange
        _mockRepo
            .Setup(r => r.GetByKeyAsync(SettingsKeys.MalApiBaseUrl))
            .ReturnsAsync((PropertyBag?)null);

        // Act
        var result = await _sut.UpdateListStatusAsync("12345", "watching", 5, 8);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MalConstants.Errors.NotConfigured);
    }
}
