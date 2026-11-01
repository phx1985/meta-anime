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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
    private readonly Mock<IServiceScopeFactory> _mockScopeFactory;
    private int _scopeCreateCount;
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

        // Each provider must be fetched inside its own DI scope (own DbContext).
        _mockScopeFactory = new Mock<IServiceScopeFactory>();
        _mockScopeFactory
            .Setup(f => f.CreateScope())
            .Returns(() =>
            {
                _scopeCreateCount++;
                return BuildScope(new Dictionary<Type, object>
                {
                    [typeof(IPlexAnimeSourceService<PlexMedia>)] = _mockPlexService.Object,
                    [typeof(IMalAnimeSourceService<MalAnime>)] = _mockMalService.Object,
                    [typeof(ISonarrAnimeSourceService<SonarrSeries>)] = _mockSonarrService.Object,
                });
            });

        _mockAnimeRepository.Setup(x => x.MergeBySourceAsync(It.IsAny<AnimeSource>(), It.IsAny<IEnumerable<Anime>>()))
            .Returns(Task.CompletedTask);
        _mockAnimeRepository.Setup(x => x.GetAllAsync()).ReturnsAsync(Enumerable.Empty<Anime>());
        _mockCacheService.Setup(x => x.MergeBySourceAsync(It.IsAny<AnimeSource>(), It.IsAny<IEnumerable<ICacheable>>()))
            .ReturnsAsync(Result.Success());
        _mockCacheService.Setup(x => x.GetAsync(It.IsAny<AnimeSource>()))
            .ReturnsAsync(Result.Success(Enumerable.Empty<Domain.Entities.CacheEntry>()));

        _sut = new AnimeService(
            _mockAnimeRepository.Object,
            _mockCacheService.Object,
            _mockPropertyBagRepository.Object,
            _mockScopeFactory.Object,
            Mock.Of<ILogger<AnimeService>>()
        );
    }

    private static IServiceScope BuildScope(Dictionary<Type, object> services)
    {
        var provider = new Mock<IServiceProvider>();
        provider.Setup(p => p.GetService(It.IsAny<Type>())).Returns((Type t) => services.GetValueOrDefault(t)!);
        var scope = new Mock<IServiceScope>();
        scope.Setup(s => s.ServiceProvider).Returns(provider.Object);
        return scope.Object;
    }

    private void SetupEmptyProviderResults()
    {
        _mockPlexService
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(Result.Success<IList<PlexMedia>>([]));
        _mockMalService
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(Result.Success<IList<MalAnime>>([]));
        _mockSonarrService
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(Result.Success<IList<SonarrSeries>>([]));
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
    public async Task RefreshData_DoesNotFetchCodecDistributions_DeferredToBackgroundJob()
    {
        // Arrange
        SetupEmptyProviderResults();
        var sonarrSeries = Enumerable.Range(1, 5)
            .Select(id => new SonarrSeries { Id = id, Title = $"Anime {id}", Tags = [1] })
            .ToList();
        _mockSonarrService
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(Result.Success<IList<SonarrSeries>>(sonarrSeries));

        // Act
        var result = await _sut.RefreshData();

        // Assert: codec fetching must never block the sync critical path.
        result.IsSuccess.Should().BeTrue();
        _mockSonarrService.Verify(
            x => x.GetBulkCodecDistributionsAsync(It.IsAny<IEnumerable<string>>()),
            Times.Never);
        _mockSonarrService.Verify(x => x.GetCodecDistributionAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RefreshData_CarriesOverCachedCodecForUnchangedSeries()
    {
        // Arrange: a cached Sonarr entry with codec data whose files did not change.
        SetupEmptyProviderResults();
        var series = new SonarrSeries { Id = 42, Title = "Anime 42", Tags = [1] };
        _mockSonarrService
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(Result.Success<IList<SonarrSeries>>([series]));

        var previous = new SonarrSeries
        {
            Id = 42,
            Title = "Anime 42",
            Tags = [1],
            CodecDistribution = new Dictionary<string, int> { ["h264"] = 3 },
        };
        _mockCacheService
            .Setup(x => x.GetAsync(AnimeSource.Sonarr))
            .ReturnsAsync(Result.Success(Enumerable.Empty<Domain.Entities.CacheEntry>().Append(
                new Domain.Entities.CacheEntry
                {
                    SourceId = "42",
                    AnimeSource = AnimeSource.Sonarr,
                    RawData = System.Text.Json.JsonSerializer.Serialize(previous),
                })));

        // Act
        var result = await _sut.RefreshData();

        // Assert: the cached codec survives the sync without any codec fetch.
        result.IsSuccess.Should().BeTrue();
        series.CodecDistribution.Should().ContainKey("h264");
        _mockSonarrService.Verify(
            x => x.GetBulkCodecDistributionsAsync(It.IsAny<IEnumerable<string>>()),
            Times.Never);
    }

    [Fact]
    public async Task RefreshData_DropsCachedCodecWhenFilesChanged()
    {
        // Arrange: cached entry has codec but different file statistics → codec must not carry over.
        SetupEmptyProviderResults();
        var series = new SonarrSeries
        {
            Id = 42,
            Title = "Anime 42",
            Tags = [1],
            Statistics = new Statistics { EpisodeFileCount = 12, TotalEpisodeCount = 12 },
        };
        _mockSonarrService
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(Result.Success<IList<SonarrSeries>>([series]));

        var previous = new SonarrSeries
        {
            Id = 42,
            Title = "Anime 42",
            Tags = [1],
            Statistics = new Statistics { EpisodeFileCount = 10, TotalEpisodeCount = 12 },
            CodecDistribution = new Dictionary<string, int> { ["h264"] = 3 },
        };
        _mockCacheService
            .Setup(x => x.GetAsync(AnimeSource.Sonarr))
            .ReturnsAsync(Result.Success(Enumerable.Empty<Domain.Entities.CacheEntry>().Append(
                new Domain.Entities.CacheEntry
                {
                    SourceId = "42",
                    AnimeSource = AnimeSource.Sonarr,
                    RawData = System.Text.Json.JsonSerializer.Serialize(previous),
                })));

        // Act
        var result = await _sut.RefreshData();

        // Assert: stale codec is dropped so the background job re-fetches it.
        result.IsSuccess.Should().BeTrue();
        series.CodecDistribution.Should().BeEmpty();
    }

    [Fact]
    public async Task RefreshData_FetchesAllProvidersConcurrently_EachInItsOwnScope()
    {
        // Arrange
        SetupEmptyProviderResults();

        // Act
        var result = await _sut.RefreshData();

        // Assert
        result.IsSuccess.Should().BeTrue();
        _mockMalService.Verify(x => x.GetAllAsync(), Times.Once);
        _mockPlexService.Verify(x => x.GetAllAsync(), Times.Once);
        _mockSonarrService.Verify(x => x.GetAllAsync(), Times.Once);
        // One isolated scope per provider (scoped DbContext cannot be shared across tasks).
        _scopeCreateCount.Should().Be(3);
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
        SetupEmptyProviderResults();

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
        SetupEmptyProviderResults();

        // Act
        var result = await _sut.RefreshData();

        // Assert
        result.IsSuccess.Should().BeTrue();
        _mockSonarrService.Verify(x => x.GetAllAsync(), Times.Never);
        _mockSonarrService.Verify(x => x.GetBulkCodecDistributionsAsync(It.IsAny<IEnumerable<string>>()), Times.Never);
    }

    [Fact]
    public async Task RefreshData_WhenProviderFails_AbortsWithoutPersisting()
    {
        // Arrange
        SetupEmptyProviderResults();
        _mockMalService
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(Result.Failure<IList<MalAnime>>(new Error("Error.Mal", "MAL down")));

        // Act
        var result = await _sut.RefreshData();

        // Assert: nothing is merged into the DB when a provider fails.
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Error.Mal");
        _mockAnimeRepository.Verify(
            x => x.MergeBySourceAsync(It.IsAny<AnimeSource>(), It.IsAny<IEnumerable<Anime>>()),
            Times.Never);
    }
}
