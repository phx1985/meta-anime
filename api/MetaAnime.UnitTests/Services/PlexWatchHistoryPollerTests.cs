using System.Net;
using System.Text;
using MetaAnime.Application.Services;
using MetaAnime.Core;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace MetaAnime.UnitTests.Services;

public class PlexWatchHistoryPollerTests
{
    /// <summary>Serves a canned Plex history payload; records the requests made.</summary>
    private sealed class StubHandler(string json, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    /// <summary>Kometa knows tvdb 403884 / imdb tt1234567 → AniDb 1 → MAL 52991.</summary>
    private static Mock<IKometaAnimeMappingRepository> Kometa()
    {
        var kometa = new Mock<IKometaAnimeMappingRepository>();
        kometa.Setup(k => k.GetAllAsync()).ReturnsAsync(new List<KometaAnimeMapping>
        {
            new() { AniDbId = "1", PlatformName = "mal_id", PlatformId = "52991" },
            new() { AniDbId = "1", PlatformName = "tvdb_id", PlatformId = "403884" },
            new() { AniDbId = "1", PlatformName = "imdb_id", PlatformId = "tt1234567" },
        });
        return kometa;
    }

    private static (PlexWatchHistoryPoller Poller, Mock<IActivityLogRepository> Repo) Create(
        string json, bool configured = true, HttpStatusCode status = HttpStatusCode.OK, string? animeSections = null)
    {
        var propertyBag = new Mock<IPropertyBagRepository>();
        propertyBag.Setup(p => p.GetByKeyAsync(SettingsKeys.PlexAnimeSections))
            .ReturnsAsync(animeSections is null
                ? null
                : new PropertyBag { Key = SettingsKeys.PlexAnimeSections, Value = animeSections });
        propertyBag.Setup(p => p.GetByKeyAsync(SettingsKeys.PlexHost))
            .ReturnsAsync(configured ? new PropertyBag { Key = SettingsKeys.PlexHost, Value = "http://plex.local:32400" } : null);
        propertyBag.Setup(p => p.GetByKeyAsync(SettingsKeys.PlexToken))
            .ReturnsAsync(configured ? new PropertyBag { Key = SettingsKeys.PlexToken, Value = "token" } : null);

        var repo = new Mock<IActivityLogRepository>();
        var activityLog = new ActivityLogService(repo.Object, propertyBag.Object, Mock.Of<ICacheEntryRepository>(), Mock.Of<IAnimeMappingRepository>(), Mock.Of<ISonarrHistoryEventRepository>(), NullLogger<ActivityLogService>.Instance);

        var poller = new PlexWatchHistoryPoller(
            new StubFactory(new StubHandler(json, status)),
            propertyBag.Object,
            Mock.Of<ICacheEntryRepository>(),
            Kometa().Object,
            activityLog,
            NullLogger<PlexWatchHistoryPoller>.Instance);

        return (poller, repo);
    }

    private static string HistoryJson(long viewedAt) => $$"""
        {
          "MediaContainer": {
            "size": 1,
            "Metadata": [
              {
                "historyKey": "4711",
                "ratingKey": "9001",
                "grandparentRatingKey": "501",
                "type": "episode",
                "grandparentTitle": "Sousou no Frieren",
                "parentIndex": 1,
                "index": 3,
                "Guid": [ { "id": "tvdb://403884" } ],
                "viewedAt": {{viewedAt}}
              }
            ]
          }
        }
        """;

    [Fact]
    public async Task PollAsync_SkipsWatchesOutsideTheConfiguredAnimeSections()
    {
        // Plex history is server-wide: without the anime-section filter the feed fills with
        // whatever else lives on the server (Miraculous and friends).
        var recent = DateTimeOffset.UtcNow.AddHours(-2).ToUnixTimeSeconds();
        var json = $$"""
        {
          "MediaContainer": {
            "size": 2,
            "Metadata": [
              { "historyKey": "1", "ratingKey": "10", "type": "episode", "grandparentTitle": "Miraculous",
                "parentIndex": 1, "index": 1, "librarySectionID": 5, "Guid": [ { "id": "tvdb://999" } ], "viewedAt": {{recent}} },
              { "historyKey": "2", "ratingKey": "20", "type": "episode", "grandparentTitle": "Sousou no Frieren",
                "parentIndex": 1, "index": 3, "librarySectionID": 3, "Guid": [ { "id": "tvdb://403884" } ], "viewedAt": {{recent}} }
            ]
          }
        }
        """;

        List<ActivityLog> written = [];
        var propertyBag = new Mock<IPropertyBagRepository>();
        propertyBag.Setup(p => p.GetByKeyAsync(SettingsKeys.PlexHost))
            .ReturnsAsync(new PropertyBag { Key = SettingsKeys.PlexHost, Value = "http://plex.local:32400" });
        propertyBag.Setup(p => p.GetByKeyAsync(SettingsKeys.PlexToken))
            .ReturnsAsync(new PropertyBag { Key = SettingsKeys.PlexToken, Value = "token" });
        propertyBag.Setup(p => p.GetByKeyAsync(SettingsKeys.PlexAnimeSections))
            .ReturnsAsync(new PropertyBag { Key = SettingsKeys.PlexAnimeSections, Value = "3" });

        var repo = new Mock<IActivityLogRepository>();
        repo.Setup(r => r.AddAsync(It.IsAny<ActivityLog>(), It.IsAny<CancellationToken>()))
            .Callback<ActivityLog, CancellationToken>((entry, _) => written.Add(entry))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.ExistsByExternalEventKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var activityLog = new ActivityLogService(repo.Object, propertyBag.Object, Mock.Of<ICacheEntryRepository>(),
            Mock.Of<IAnimeMappingRepository>(), Mock.Of<ISonarrHistoryEventRepository>(), NullLogger<ActivityLogService>.Instance);
        var poller = new PlexWatchHistoryPoller(
            new StubFactory(new StubHandler(json)), propertyBag.Object, Mock.Of<ICacheEntryRepository>(), Kometa().Object, activityLog,
            NullLogger<PlexWatchHistoryPoller>.Instance);

        await poller.PollAsync();

        var entry = Assert.Single(written);
        Assert.Equal("Sousou no Frieren", entry.Title);
    }

    [Fact]
    public async Task PollAsync_WhenNewWatchesExist_LogsEpisodeWatchedIdempotently()
    {
        var viewedAt = DateTimeOffset.UtcNow.AddHours(-3).ToUnixTimeSeconds();
        var (poller, repo) = Create(HistoryJson(viewedAt));

        ActivityLog? captured = null;
        repo.Setup(r => r.ExistsByExternalEventKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        repo.Setup(r => r.AddAsync(It.IsAny<ActivityLog>(), It.IsAny<CancellationToken>()))
            .Callback<ActivityLog, CancellationToken>((entry, _) => captured = entry)
            .Returns(Task.CompletedTask);

        var count = await poller.PollAsync();

        Assert.Equal(1, count);
        Assert.NotNull(captured);
        Assert.Equal(ActivityType.EpisodeWatched, captured!.Type);
        Assert.Equal("Sousou no Frieren", captured.Title);
        Assert.Equal("S01E03 watched on Plex", captured.Subtitle);
        Assert.Equal("plex-hist-4711", captured.ExternalEventKey);
        Assert.Contains("plex", captured.MetadataJson);
    }

    [Fact]
    public async Task PollAsync_SkipsWatchesOlderThanTheLookback()
    {
        // The lookback is a year now (so "Year" can show real watch counts).
        var viewedAt = DateTimeOffset.UtcNow.AddDays(-400).ToUnixTimeSeconds();
        var (poller, repo) = Create(HistoryJson(viewedAt));

        var count = await poller.PollAsync();

        Assert.Equal(0, count);
        repo.Verify(r => r.AddAsync(It.IsAny<ActivityLog>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PollAsync_WhenPlexIsNotConfigured_DoesNothing()
    {
        var (poller, repo) = Create(HistoryJson(DateTimeOffset.UtcNow.ToUnixTimeSeconds()), configured: false);

        Assert.Equal(0, await poller.PollAsync());
        repo.Verify(r => r.AddAsync(It.IsAny<ActivityLog>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PollAsync_WhenTheServerErrors_NeverThrows()
    {
        // An offline Plex must not fail the download-stats job that owns the poll.
        var (poller, repo) = Create("{}", status: HttpStatusCode.InternalServerError);

        Assert.Equal(0, await poller.PollAsync());
        repo.Verify(r => r.AddAsync(It.IsAny<ActivityLog>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
