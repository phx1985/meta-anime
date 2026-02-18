using FluentAssertions;
using MetaAnime.Application.Services;
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
    private readonly AnimeService _sut;

    public AnimeServiceTests()
    {
        _mockAnimeRepository = new Mock<IAnimeRepository>();
        _mockPlexService = new Mock<IPlexAnimeSourceService<PlexMedia>>();
        _mockMalService = new Mock<IMalAnimeSourceService<MalAnime>>();
        _mockSonarrService = new Mock<ISonarrAnimeSourceService<SonarrSeries>>();
        _mockCacheService = new Mock<ICacheEntryService>();
        _sut = new AnimeService(
            _mockAnimeRepository.Object,
            _mockPlexService.Object,
            _mockMalService.Object,
            _mockSonarrService.Object,
            _mockCacheService.Object
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
}
