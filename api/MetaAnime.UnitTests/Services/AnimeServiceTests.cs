using FluentAssertions;
using MetaAnime.Application.Services;
using MetaAnime.Core;
using MetaAnime.Core.Interfaces.Caching;
using MetaAnime.Core.Interfaces.Mappers;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Interfaces.Shared.Results;
using MetaAnime.Domain.Entities;
using MetaAnime.Domain.Enums;
using MetaAnime.Infrastructure.MyAnimeList.Models;
using MetaAnime.Infrastructure.Plex.Models;
using MetaAnime.Infrastructure.SonarrAPIs.Sonarr.Models;
using Moq;

namespace MetaAnime.UnitTests.Services;

public class AnimeServiceTests
{
    private readonly Mock<IAnimeRepository> _mockAnimeRepository;
    private readonly Mock<IPlexAnimeSourceService<PlexMedia>> _mockPlexService;
    private readonly Mock<IMalAnimeSourceService<MalAnime>> _mockMalService;
    private readonly Mock<ISonarrAnimeSourceService<SonarrSeries>> _mockSonarrService;
    private readonly Mock<ICacheEntryService> _mockCacheService;
    private readonly Mock<IPropertyBagRepository> _mockPropertyBagRepository;
    private readonly AnimeService _sut;

    public AnimeServiceTests()
    {
        _mockAnimeRepository = new Mock<IAnimeRepository>();
        _mockPlexService = new Mock<IPlexAnimeSourceService<PlexMedia>>();
        _mockMalService = new Mock<IMalAnimeSourceService<MalAnime>>();
        _mockSonarrService = new Mock<ISonarrAnimeSourceService<SonarrSeries>>();
        _mockCacheService = new Mock<ICacheEntryService>();
        _mockPropertyBagRepository = new Mock<IPropertyBagRepository>();

        // Default: both providers enabled
        _mockPropertyBagRepository
            .Setup(x => x.GetByKeyAsync(SettingsKeys.PlexEnabled))
            .ReturnsAsync(new PropertyBag { Key = SettingsKeys.PlexEnabled, Value = "true" });
        _mockPropertyBagRepository
            .Setup(x => x.GetByKeyAsync(SettingsKeys.SonarrEnabled))
            .ReturnsAsync(new PropertyBag { Key = SettingsKeys.SonarrEnabled, Value = "true" });

        _sut = new AnimeService(
            _mockAnimeRepository.Object,
            _mockPlexService.Object,
            _mockMalService.Object,
            _mockSonarrService.Object,
            _mockCacheService.Object,
            _mockPropertyBagRepository.Object
        );
    }

    [Fact]
    public async Task GetByIdAsync_WhenAnimeExists_ReturnsSuccessResult()
    {
        // Arrange
        var animeId = Guid.NewGuid();
        var expectedAnime = new Anime
        {
            Id = animeId,
            SourceId = "test123",
            Title = "Test Anime",
            AnimeSource = AnimeSource.MyAnimeList
        };

        _mockAnimeRepository
            .Setup(x => x.GetByIdAsync(animeId))
            .ReturnsAsync(expectedAnime);

        // Act
        var result = await _sut.GetByIdAsync(animeId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Id.Should().Be(animeId);
        result.Value.Title.Should().Be("Test Anime");
    }

    [Fact]
    public async Task GetByIdAsync_WhenAnimeDoesNotExist_ReturnsNullValue()
    {
        // Arrange
        var animeId = Guid.NewGuid();
        _mockAnimeRepository
            .Setup(x => x.GetByIdAsync(animeId))
            .ReturnsAsync((Anime?)null);

        // Act
        var result = await _sut.GetByIdAsync(animeId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task RefreshData_FetchesCodecDistributionForAllSonarrSeriesInParallel()
    {
        // Arrange
        var seriesIds = Enumerable.Range(1, 5).ToList();
        var sonarrSeries = seriesIds
            .Select(id => new SonarrSeries { Id = id, Title = $"Anime {id}", Tags = [1] })
            .ToList();

        _mockPlexService
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(Result.Success<IList<PlexMedia>>([]));

        _mockMalService
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(Result.Success<IList<MalAnime>>([]));

        _mockSonarrService
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(Result.Success<IList<SonarrSeries>>(sonarrSeries));

        var bulkResult = seriesIds.ToDictionary(
            id => id.ToString(),
            id => new Dictionary<string, int> { ["h264"] = id });

        _mockSonarrService
            .Setup(x => x.GetBulkCodecDistributionsAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(bulkResult);

        _mockAnimeRepository.Setup(x => x.BulkDeleteAllAsync()).Returns(Task.CompletedTask);
        _mockAnimeRepository.Setup(x => x.BulkInsertAsync(It.IsAny<IEnumerable<IAnimeConvertible>>())).Returns(Task.CompletedTask);
        _mockAnimeRepository.Setup(x => x.GetAllAsync()).ReturnsAsync(Enumerable.Empty<Anime>());

        _mockCacheService.Setup(x => x.BulkDeleteBySourceAsync(It.IsAny<AnimeSource>())).ReturnsAsync(Result.Success());
        _mockCacheService.Setup(x => x.BulkInsertAsync(It.IsAny<IEnumerable<ICacheable>>())).ReturnsAsync(Result.Success());

        // Act
        var result = await _sut.RefreshData();

        // Assert
        result.IsSuccess.Should().BeTrue();
        _mockSonarrService.Verify(
            x => x.GetBulkCodecDistributionsAsync(It.IsAny<IEnumerable<string>>()),
            Times.Once);
        sonarrSeries.All(s => s.CodecDistribution.ContainsKey("h264")).Should().BeTrue();
    }

    [Fact]
    public async Task BulkInsertAsync_WithValidAnimes_ReturnsSuccessResult()
    {
        // Arrange
        var animes = new List<Anime>
        {
            new Anime
            {
                SourceId = "test1",
                Title = "Test Anime 1",
                AnimeSource = AnimeSource.Plex
            },
            new Anime
            {
                SourceId = "test2",
                Title = "Test Anime 2",
                AnimeSource = AnimeSource.Sonarr
            }
        };

        _mockAnimeRepository
            .Setup(x => x.BulkInsertAsync(animes))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.BulkInsertAsync(animes);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _mockAnimeRepository.Verify(x => x.BulkInsertAsync(animes), Times.Once);
    }

    [Fact]
    public async Task RefreshData_WhenPlexDisabled_SkipsPlexSync()
    {
        // Arrange
        _mockPropertyBagRepository
            .Setup(x => x.GetByKeyAsync(SettingsKeys.PlexEnabled))
            .ReturnsAsync(new PropertyBag { Key = SettingsKeys.PlexEnabled, Value = "false" });

        _mockMalService
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(Result.Success<IList<MalAnime>>([]));

        _mockSonarrService
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(Result.Success<IList<SonarrSeries>>([]));

        _mockSonarrService
            .Setup(x => x.GetBulkCodecDistributionsAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new Dictionary<string, Dictionary<string, int>>());

        _mockAnimeRepository.Setup(x => x.BulkDeleteAllAsync()).Returns(Task.CompletedTask);
        _mockAnimeRepository.Setup(x => x.BulkInsertAsync(It.IsAny<IEnumerable<IAnimeConvertible>>())).Returns(Task.CompletedTask);
        _mockAnimeRepository.Setup(x => x.GetAllAsync()).ReturnsAsync(Enumerable.Empty<Anime>());

        _mockCacheService.Setup(x => x.BulkDeleteBySourceAsync(It.IsAny<AnimeSource>())).ReturnsAsync(Result.Success());
        _mockCacheService.Setup(x => x.BulkInsertAsync(It.IsAny<IEnumerable<ICacheable>>())).ReturnsAsync(Result.Success());

        // Act
        var result = await _sut.RefreshData();

        // Assert
        result.IsSuccess.Should().BeTrue();
        _mockPlexService.Verify(x => x.GetAllAsync(), Times.Never);
    }

    [Fact]
    public async Task RefreshData_WhenSonarrDisabled_SkipsSonarrSync()
    {
        // Arrange
        _mockPropertyBagRepository
            .Setup(x => x.GetByKeyAsync(SettingsKeys.SonarrEnabled))
            .ReturnsAsync(new PropertyBag { Key = SettingsKeys.SonarrEnabled, Value = "false" });

        _mockPlexService
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(Result.Success<IList<PlexMedia>>([]));

        _mockMalService
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(Result.Success<IList<MalAnime>>([]));

        _mockAnimeRepository.Setup(x => x.BulkDeleteAllAsync()).Returns(Task.CompletedTask);
        _mockAnimeRepository.Setup(x => x.BulkInsertAsync(It.IsAny<IEnumerable<IAnimeConvertible>>())).Returns(Task.CompletedTask);
        _mockAnimeRepository.Setup(x => x.GetAllAsync()).ReturnsAsync(Enumerable.Empty<Anime>());

        _mockCacheService.Setup(x => x.BulkDeleteBySourceAsync(It.IsAny<AnimeSource>())).ReturnsAsync(Result.Success());
        _mockCacheService.Setup(x => x.BulkInsertAsync(It.IsAny<IEnumerable<ICacheable>>())).ReturnsAsync(Result.Success());

        // Act
        var result = await _sut.RefreshData();

        // Assert
        result.IsSuccess.Should().BeTrue();
        _mockSonarrService.Verify(x => x.GetAllAsync(), Times.Never);
        _mockSonarrService.Verify(x => x.GetBulkCodecDistributionsAsync(It.IsAny<IEnumerable<string>>()), Times.Never);
    }
}
