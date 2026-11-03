using FluentAssertions;
using MetaAnime.Application.BackgroundJobs;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Interfaces.Shared.Results;
using MetaAnime.Domain.Enums;
using MetaAnime.Infrastructure.SonarrAPIs.Sonarr.Models;
using Microsoft.Extensions.Logging;
using Moq;

namespace MetaAnime.UnitTests.Services;

public class SonarrCodecEnrichmentJobTests
{
    private readonly Mock<ICacheEntryService> _cacheService = new();
    private readonly Mock<ISonarrAnimeSourceService<SonarrSeries>> _sonarrService = new();
    private readonly SonarrCodecEnrichmentJob _sut;

    public SonarrCodecEnrichmentJobTests()
    {
        _sut = new SonarrCodecEnrichmentJob(
            _cacheService.Object,
            _sonarrService.Object,
            Mock.Of<ILogger<SonarrCodecEnrichmentJob>>());
    }

    private static Domain.Entities.CacheEntry EntryFor(SonarrSeries series) => new()
    {
        SourceId = series.Id.ToString(),
        AnimeSource = AnimeSource.Sonarr,
        RawData = System.Text.Json.JsonSerializer.Serialize(series),
    };

    [Fact]
    public async Task EnrichAsync_FetchesCodecOnlyForEntriesWithoutCodecData()
    {
        var withCodec = new SonarrSeries { Id = 1, Title = "A" };
        withCodec.CodecDistribution = new Dictionary<string, int> { ["h264"] = 5 };
        var withoutCodec = new SonarrSeries { Id = 2, Title = "B", Statistics = new Statistics { EpisodeFileCount = 3 } };

        _cacheService
            .Setup(s => s.GetAsync(AnimeSource.Sonarr))
            .ReturnsAsync(Result.Success(Enumerable.Empty<Domain.Entities.CacheEntry>()
                .Append(EntryFor(withCodec))
                .Append(EntryFor(withoutCodec))));
        _sonarrService
            .Setup(s => s.GetBulkCodecDistributionsAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new Dictionary<string, Dictionary<string, int>>
            {
                ["2"] = new() { ["hevc"] = 3 },
            });
        _cacheService
            .Setup(s => s.BulkUpdateRawDataAsync(AnimeSource.Sonarr, It.IsAny<IEnumerable<(string, string)>>()))
            .ReturnsAsync(Result.Success());

        await _sut.EnrichAsync();

        _sonarrService.Verify(
            s => s.GetBulkCodecDistributionsAsync(It.Is<IEnumerable<string>>(ids => ids.Single() == "2")),
            Times.Once);
        _cacheService.Verify(
            s => s.BulkUpdateRawDataAsync(
                AnimeSource.Sonarr,
                It.Is<IEnumerable<(string SourceId, string RawData)>>(u => u.Single().SourceId == "2")),
            Times.Once);
    }

    [Fact]
    public async Task EnrichAsync_WhenAllEntriesHaveCodec_DoesNotFetch()
    {
        var withCodec = new SonarrSeries { Id = 1, Title = "A" };
        withCodec.CodecDistribution = new Dictionary<string, int> { ["h264"] = 5 };

        _cacheService
            .Setup(s => s.GetAsync(AnimeSource.Sonarr))
            .ReturnsAsync(Result.Success(Enumerable.Empty<Domain.Entities.CacheEntry>().Append(EntryFor(withCodec))));

        await _sut.EnrichAsync();

        _sonarrService.Verify(
            s => s.GetBulkCodecDistributionsAsync(It.IsAny<IEnumerable<string>>()),
            Times.Never);
        _cacheService.Verify(
            s => s.BulkUpdateRawDataAsync(It.IsAny<AnimeSource>(), It.IsAny<IEnumerable<(string, string)>>()),
            Times.Never);
    }

    [Fact]
    public async Task EnrichAsync_SkipsSeriesWithoutEpisodeFiles_Entirely()
    {
        // Unaired/empty series can never have codec data — they must not trigger API calls
        // or row rewrites on every cycle.
        var noFiles = new SonarrSeries { Id = 9, Title = "Unaired", Statistics = new Statistics { EpisodeFileCount = 0 } };

        _cacheService
            .Setup(s => s.GetAsync(AnimeSource.Sonarr))
            .ReturnsAsync(Result.Success(Enumerable.Empty<Domain.Entities.CacheEntry>().Append(EntryFor(noFiles))));

        await _sut.EnrichAsync();

        _sonarrService.Verify(
            s => s.GetBulkCodecDistributionsAsync(It.IsAny<IEnumerable<string>>()),
            Times.Never);
        _cacheService.Verify(
            s => s.BulkUpdateRawDataAsync(It.IsAny<AnimeSource>(), It.IsAny<IEnumerable<(string, string)>>()),
            Times.Never);
    }

    [Fact]
    public async Task EnrichAsync_WritesCodecIntoRawData_InOneBatchedCall()
    {
        var withoutCodec = new SonarrSeries { Id = 2, Title = "B", Statistics = new Statistics { EpisodeFileCount = 1 } };
        IEnumerable<(string SourceId, string RawData)>? written = null;

        _cacheService
            .Setup(s => s.GetAsync(AnimeSource.Sonarr))
            .ReturnsAsync(Result.Success(Enumerable.Empty<Domain.Entities.CacheEntry>().Append(EntryFor(withoutCodec))));
        _sonarrService
            .Setup(s => s.GetBulkCodecDistributionsAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new Dictionary<string, Dictionary<string, int>>
            {
                ["2"] = new() { ["av1"] = 2 },
            });
        _cacheService
            .Setup(s => s.BulkUpdateRawDataAsync(AnimeSource.Sonarr, It.IsAny<IEnumerable<(string, string)>>()))
            .Callback<AnimeSource, IEnumerable<(string, string)>>((_, u) => written = u)
            .ReturnsAsync(Result.Success());

        await _sut.EnrichAsync();

        written.Should().NotBeNull();
        var (sourceId, rawData) = written!.Single();
        sourceId.Should().Be("2");
        var updated = System.Text.Json.JsonSerializer.Deserialize<SonarrSeries>(rawData);
        updated!.CodecDistribution.Should().ContainKey("av1");
    }

    [Fact]
    public async Task EnrichAsync_EmptyDistribution_DoesNotRewriteTheRow()
    {
        var withoutCodec = new SonarrSeries { Id = 2, Title = "B", Statistics = new Statistics { EpisodeFileCount = 1 } };

        _cacheService
            .Setup(s => s.GetAsync(AnimeSource.Sonarr))
            .ReturnsAsync(Result.Success(Enumerable.Empty<Domain.Entities.CacheEntry>().Append(EntryFor(withoutCodec))));
        _sonarrService
            .Setup(s => s.GetBulkCodecDistributionsAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new Dictionary<string, Dictionary<string, int>> { ["2"] = new() });

        await _sut.EnrichAsync();

        _cacheService.Verify(
            s => s.BulkUpdateRawDataAsync(It.IsAny<AnimeSource>(), It.IsAny<IEnumerable<(string, string)>>()),
            Times.Never);
    }
}
