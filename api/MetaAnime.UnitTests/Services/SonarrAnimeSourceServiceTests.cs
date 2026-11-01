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
}
