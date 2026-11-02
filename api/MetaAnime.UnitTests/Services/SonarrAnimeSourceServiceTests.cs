using FluentAssertions;
using MetaAnime.Core;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Domain.Entities;
using MetaAnime.Infrastructure.Sonarr.Services;
using Moq;
using System.Net;
using System.Net.Http;

namespace MetaAnime.UnitTests.Services;

public class SonarrAnimeSourceServiceTests
{
    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(responder(request));
        }
    }

    private sealed class RecordingFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private static Mock<IPropertyBagRepository> PropertyBag()
    {
        var propertyBag = new Mock<IPropertyBagRepository>();
        propertyBag
            .Setup(p => p.GetByKeyAsync(It.IsAny<string>()))
            .ReturnsAsync((string key) => key switch
            {
                SettingsKeys.SonarrHost => new PropertyBag { Key = key, Value = "http://sonarr.test" },
                SettingsKeys.SonarrApiKey => new PropertyBag { Key = key, Value = "key" },
                SettingsKeys.SonarrAnimeTags => new PropertyBag { Key = key, Value = "anime" },
                _ => null,
            });
        return propertyBag;
    }

    [Fact]
    public async Task GetBulkCodecDistributionsAsync_FetchesPerSeries_AndGroupsCorrectly()
    {
        // Sonarr's api/v3/episodefile REQUIRES one call per series (it rejects requests
        // without seriesId and its episodeFileIds variant throws on unknown ids) — so the
        // callers' contract is to request ONLY series that need codec data. This test pins
        // the per-series grouping semantics.
        var filesBySeries = new Dictionary<string, string>
        {
            ["10"] = """[{"id":1,"seriesId":10,"size":100,"mediaInfo":{"videoCodec":"h264"}},{"id":2,"seriesId":10,"size":200,"mediaInfo":{"videoCodec":"h264"}}]""",
            ["20"] = """[{"id":3,"seriesId":20,"size":300,"mediaInfo":{"videoCodec":"hevc"}}]""",
            ["30"] = """[]""",
        };
        var handler = new RecordingHandler(request =>
        {
            var seriesId = request.RequestUri!.Query.Split('&')
                .Select(q => q.Split('='))
                .FirstOrDefault(p => p[0] == "?seriesId" || p[0] == "seriesId")?[1] ?? "";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(filesBySeries.GetValueOrDefault(seriesId, "[]")),
            };
        });
        var service = new SonarrAnimeSourceService(PropertyBag().Object, new RecordingFactory(new HttpClient(handler)));

        var result = await service.GetBulkCodecDistributionsAsync(["10", "20", "30"]);

        result.Should().ContainKeys("10", "20", "30");
        result["10"]["h264"].Should().Be(2);
        result["20"]["hevc"].Should().Be(1);
        result["30"].Should().BeEmpty();
        // One request per requested series — forced by the Sonarr API (see method doc).
        handler.Requests.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetSeriesByCodecAsync_FiltersByCodec_AndCountsSizes()
    {
        var handler = new RecordingHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            var query = request.RequestUri!.Query;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(path switch
                {
                    var p when p.EndsWith("/tag") => """[{"id":1,"label":"anime"}]""",
                    var p when p.EndsWith("/series") => """[{"id":10,"title":"A","tags":[1],"year":2020},{"id":20,"title":"B","tags":[1],"year":2021}]""",
                    _ when query.Contains("seriesId=10") => """[{"id":1,"seriesId":10,"size":100,"mediaInfo":{"videoCodec":"h264"}}]""",
                    _ => """[{"id":2,"seriesId":20,"size":300,"mediaInfo":{"videoCodec":"hevc"}}]""",
                }),
            };
        });
        var service = new SonarrAnimeSourceService(PropertyBag().Object, new RecordingFactory(new HttpClient(handler)));

        var result = await service.GetSeriesByCodecAsync("h264");

        result.IsSuccess.Should().BeTrue();
        var match = result.Value.Should().ContainSingle().Subject;
        match.Title.Should().Be("A");
        match.TotalSizeOnDisk.Should().Be(100);
    }

    [Fact]
    public async Task GetDownloadHistorySinceAsync_UsesTheSinceEndpoint_AndProjectsIdDateAndSize()
    {
        // The cache job's whole speed-up rests on this: ONE unsorted api/v3/history/since call
        // (date-filtered) instead of paging the sorted api/v3/history table, which sorts the
        // entire history on every page (~10s per 2 000 records on a 34k-row history).
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                [
                  {"id":901,"seriesId":12,"episodeId":340,"date":"2026-10-31T18:12:00Z","eventType":"downloadFolderImported",
                   "data":{"size":"1500000000"},
                   "series":{"id":12,"title":"Frieren"},
                   "episode":{"id":340,"seasonNumber":1,"episodeNumber":3}},
                  {"id":902,"seriesId":12,"episodeId":341,"date":"2026-11-01T02:00:00Z","eventType":"downloadFolderImported","data":{"size":250}},
                  {"id":903,"date":"2026-11-01T03:00:00Z","eventType":"grabbed","data":{}}
                ]
                """),
        });
        var service = new SonarrAnimeSourceService(PropertyBag().Object, new RecordingFactory(new HttpClient(handler)));

        var result = await service.GetDownloadHistorySinceAsync(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        result.IsSuccess.Should().BeTrue();
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.RequestUri!.AbsolutePath.Should().EndWith("/api/v3/history/since");
        request.RequestUri.Query.Should().Contain("eventType=3")
            .And.Contain("date=2026-01-01T00")
            .And.Contain("includeSeries=true")
            .And.Contain("includeEpisode=true");

        // Non-import events are filtered out; sizes come from either a numeric string or a number.
        result.Value.Should().HaveCount(2);
        result.Value[0].Id.Should().Be(901);
        result.Value[0].Date.Should().Be(new DateTime(2026, 10, 31, 18, 12, 0, DateTimeKind.Utc));
        result.Value[0].SizeBytes.Should().Be(1_500_000_000);
        result.Value[0].SeriesTitle.Should().Be("Frieren");
        result.Value[0].SeasonNumber.Should().Be(1);
        result.Value[0].EpisodeNumber.Should().Be(3);
        // Row 902 carries no embedded objects — ids fall back to the record fields, title to empty.
        result.Value[1].SeriesTitle.Should().BeEmpty();
        result.Value[1].SeriesId.Should().Be(12);
        result.Value[1].EpisodeId.Should().Be(341);
        result.Value[1].SizeBytes.Should().Be(250);
        result.Value.Should().OnlyContain(e => e.Date.Kind == DateTimeKind.Utc);
    }

    [Fact]
    public async Task GetDownloadHistorySinceAsync_NotConfigured_ReturnsEmptyWithoutCallingSonarr()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var propertyBag = new Mock<IPropertyBagRepository>();
        propertyBag.Setup(p => p.GetByKeyAsync(It.IsAny<string>())).ReturnsAsync((PropertyBag?)null);
        var service = new SonarrAnimeSourceService(propertyBag.Object, new RecordingFactory(new HttpClient(handler)));

        var result = await service.GetDownloadHistorySinceAsync(DateTime.UtcNow.AddDays(-1));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDownloadHistorySinceAsync_HttpFailure_ReturnsFailure()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var service = new SonarrAnimeSourceService(PropertyBag().Object, new RecordingFactory(new HttpClient(handler)));

        var result = await service.GetDownloadHistorySinceAsync(DateTime.UtcNow.AddDays(-1));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Sonarr.DownloadHistory");
    }
}
