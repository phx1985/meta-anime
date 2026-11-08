using MetaAnime.Application.Contracts;
using MetaAnime.Application.Services;
using MetaAnime.Core;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Domain.Entities;
using MetaAnime.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace MetaAnime.UnitTests.Services;

public class ActivityLogServiceTests
{
    private static ActivityLogService Create(Mock<IActivityLogRepository> repo) =>
        new(repo.Object, Mock.Of<IPropertyBagRepository>(), Mock.Of<ICacheEntryRepository>(), Mock.Of<IAnimeMappingRepository>(), Mock.Of<ISonarrHistoryEventRepository>(), NullLogger<ActivityLogService>.Instance);

    [Fact]
    public async Task LogAsync_PersistsEveryFieldAndSerializesMetadata()
    {
        ActivityLog? captured = null;
        var repo = new Mock<IActivityLogRepository>();
        repo.Setup(r => r.AddAsync(It.IsAny<ActivityLog>(), It.IsAny<CancellationToken>()))
            .Callback<ActivityLog, CancellationToken>((entry, _) => captured = entry)
            .Returns(Task.CompletedTask);

        await Create(repo).LogAsync(
            ActivityType.AnimeAdded, "Frieren", "Added to Sonarr",
            malId: 52991, tvdbId: 403884, posterUrl: "https://img/x.jpg",
            metadata: new { seasons = 2 });

        Assert.NotNull(captured);
        Assert.Equal(ActivityType.AnimeAdded, captured!.Type);
        Assert.Equal("Frieren", captured.Title);
        Assert.Equal("Added to Sonarr", captured.Subtitle);
        Assert.Equal(52991, captured.MalId);
        Assert.Equal(403884, captured.TvdbId);
        Assert.Contains("seasons", captured.MetadataJson);
    }

    [Fact]
    public async Task LogAsync_SwallowsRepositoryFailures()
    {
        // An activity write must never break the add/sync that produced it.
        var repo = new Mock<IActivityLogRepository>();
        repo.Setup(r => r.AddAsync(It.IsAny<ActivityLog>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));

        await Create(repo).LogAsync(ActivityType.SyncCompleted, "Metadata sync");

        repo.Verify(r => r.AddAsync(It.IsAny<ActivityLog>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetFeedAsync_ClampsPaging_AndPassesTheFilterThrough()
    {
        var repo = new Mock<IActivityLogRepository>();
        repo.Setup(r => r.CountAsync(It.IsAny<IReadOnlyCollection<ActivityType>?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>())).ReturnsAsync(7);
        repo.Setup(r => r.GetRecentAsync(It.IsAny<IReadOnlyCollection<ActivityType>?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ActivityLog>
            {
                new() { Type = ActivityType.SyncCompleted, Title = "Metadata sync", Timestamp = DateTimeOffset.UtcNow },
            });

        var result = await Create(repo).GetFeedAsync([ActivityType.AnimeAdded], page: 0, pageSize: 500);

        Assert.Equal(1, result.Page);          // page clamped to 1
        Assert.Equal(100, result.PageSize);    // pageSize clamped to 100
        Assert.Equal(7, result.Total);
        Assert.Single(result.Items);

        // Offset for page 1 is 0, and the filter reached the repository.
        repo.Verify(r => r.GetRecentAsync(It.Is<IReadOnlyCollection<ActivityType>>(t => t.Contains(ActivityType.AnimeAdded)), 0, 100, null, It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetSummaryAsync_SubtractsRevertedAdds_SoAddThenDeleteIsZero()
    {
        var repo = new Mock<IActivityLogRepository>();
        repo.Setup(r => r.CountByTypeAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<ActivityType, int>
            {
                // The repository already excludes reverted adds from the AnimeAdded count.
                [ActivityType.AnimeAdded] = 0,
                [ActivityType.AnimeRemoved] = 1,
                [ActivityType.EpisodeDownloaded] = 4,
                [ActivityType.EpisodeWatched] = 2,
            });

        // Imports are counted from the mirrored Sonarr history, not from the activity log.
        var history = new Mock<ISonarrHistoryEventRepository>();
        history.Setup(h => h.GetSinceAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SonarrHistoryEvent>
            {
                new() { Id = 1, OccurredAt = DateTime.UtcNow.AddDays(-1) },
                new() { Id = 2, OccurredAt = DateTime.UtcNow.AddDays(-2) },
                new() { Id = 3, OccurredAt = DateTime.UtcNow.AddDays(-3) },
                new() { Id = 4, OccurredAt = DateTime.UtcNow.AddDays(-4) },
            });

        var service = new ActivityLogService(
            repo.Object, Mock.Of<IPropertyBagRepository>(), Mock.Of<ICacheEntryRepository>(),
            Mock.Of<IAnimeMappingRepository>(), history.Object, NullLogger<ActivityLogService>.Instance);

        var summary = await service.GetSummaryAsync(30);

        Assert.Equal(0, summary.Added);
        Assert.Equal(1, summary.Removed);
        Assert.Equal(-1, summary.NetAdded);
        Assert.Equal(4, summary.Ingested);
        Assert.Equal(2, summary.Watched);
    }

    [Fact]
    public async Task GetSummaryAsync_WithCalendarPeriod_UsesTheChartBoundary_NotARollingWindow()
    {
        // The range pills drive the tiles AND the chart: a rolling "last 30 days" here would
        // make the tiles disagree with the calendar windows the chart plots.
        var repo = new Mock<IActivityLogRepository>();
        DateTimeOffset? captured = null;
        repo.Setup(r => r.CountByTypeAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .Callback<DateTimeOffset, CancellationToken>((from, _) => captured = from)
            .ReturnsAsync(new Dictionary<ActivityType, int>());

        await Create(repo).GetSummaryAsync(ActivityStatsPeriod.ThisMonth, tzOffsetMinutes: 0);

        Assert.NotNull(captured);
        Assert.Equal(1, captured!.Value.Day);
        Assert.Equal(0, captured.Value.Hour);
        Assert.Equal(TimeSpan.Zero, captured.Value.Offset);
    }

    [Fact]
    public async Task RevertAsync_NeverThrows_WhenTheRepositoryFails()
    {
        var repo = new Mock<IActivityLogRepository>();
        repo.Setup(r => r.RevertAddedAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));

        await Create(repo).RevertAsync(malId: 52991, tvdbId: 403884);

        repo.Verify(r => r.RevertAddedAsync(52991, 403884, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LogAsync_WithOccurredAt_StampsTheRowWithTheProviderTimestamp()
    {
        // Plex reports when an episode was watched; stamping it "now" would make every old
        // scrobble count as today and break the range filters on the KPI tiles.
        var repo = new Mock<IActivityLogRepository>();
        ActivityLog? written = null;
        repo.Setup(r => r.AddAsync(It.IsAny<ActivityLog>(), It.IsAny<CancellationToken>()))
            .Callback<ActivityLog, CancellationToken>((entry, _) => written = entry)
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.ExistsByExternalEventKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var watchedAt = DateTimeOffset.UtcNow.AddDays(-3);

        await Create(repo).LogAsync(
            ActivityType.EpisodeWatched, "Frieren", "S01E03 watched on Plex",
            provider: "plex", externalEventKey: "plex-hist-42", occurredAt: watchedAt);

        Assert.NotNull(written);
        Assert.Equal(watchedAt, written!.Timestamp);
    }

    [Fact]
    public async Task LogAsync_WhenTheEventKeyAlreadyExists_SkipsTheWrite()
    {
        // Pollers re-read the provider's newest events on every cycle — only new ones may land.
        var repo = new Mock<IActivityLogRepository>();
        repo.Setup(r => r.ExistsByExternalEventKeyAsync("plex-hist-4711", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await Create(repo).LogAsync(
            ActivityType.EpisodeWatched, "Frieren", "S01E03 watched on Plex", externalEventKey: "plex-hist-4711");

        repo.Verify(r => r.AddAsync(It.IsAny<ActivityLog>(), It.IsAny<CancellationToken>()), Times.Never);
        repo.Verify(r => r.UpdateAsync(It.IsAny<ActivityLog>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RecordAnimeAdded_WithinWindow_ConsolidatesSonarrAndMal()
    {
        var existing = new ActivityLog
        {
            Type = ActivityType.AnimeAdded,
            Title = "Sousou no Frieren",
            Subtitle = "Added to MyAnimeList (watching)",
            MalId = 52991,
            Timestamp = DateTimeOffset.UtcNow.AddMinutes(-2),
        };

        var repo = new Mock<IActivityLogRepository>();
        repo.Setup(r => r.FindRecentCandidateAsync(
                ActivityType.AnimeAdded, 52991, It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        await Create(repo).LogAsync(
            ActivityType.AnimeAdded, "Sousou no Frieren", "Added to Sonarr",
            malId: 52991, tvdbId: 403884, provider: "sonarr");

        repo.Verify(r => r.AddAsync(It.IsAny<ActivityLog>(), It.IsAny<CancellationToken>()), Times.Never);
        repo.Verify(r => r.UpdateAsync(It.IsAny<ActivityLog>(), It.IsAny<CancellationToken>()), Times.Once);

        Assert.Contains("Sonarr", existing.Subtitle);
        Assert.Contains("MyAnimeList", existing.Subtitle);
        Assert.Equal(403884, existing.TvdbId);
        Assert.Contains("providers", existing.MetadataJson);
        Assert.Contains("sonarr", existing.MetadataJson);
    }

    [Fact]
    public async Task GetFeedAsync_SonarrLink_UsesTheSeriesSlug_WhenIndexed()
    {
        var (service, _) = CreateWithProviders(
            tvdbId: 403884,
            seriesJson: """{"id":42,"titleSlug":"sousou-no-frieren","tvdbId":403884}""");

        var feed = await service.GetFeedAsync(null, 1, 25);

        var sonarr = Assert.Single(feed.Items[0].ProviderLinks, l => l.Provider == "sonarr");
        Assert.Equal("http://sonarr.local:8989/series/sousou-no-frieren", sonarr.ExternalUrl);
    }

    [Fact]
    public async Task GetFeedAsync_SonarrLink_FallsBackToSearch_NeverTheBareHost()
    {
        var (service, _) = CreateWithProviders(tvdbId: 999, seriesJson: """{"id":42,"titleSlug":"other","tvdbId":403884}""");

        var feed = await service.GetFeedAsync(null, 1, 25);

        var sonarr = Assert.Single(feed.Items[0].ProviderLinks, l => l.Provider == "sonarr");
        Assert.Contains("/add/search?term=", sonarr.ExternalUrl);
        Assert.NotEqual("http://sonarr.local:8989", sonarr.ExternalUrl);
        Assert.NotEqual("http://sonarr.local:8989/", sonarr.ExternalUrl);
    }

    [Fact]
    public async Task GetFeedAsync_SonarrLink_MatchesByTitle_WhenTheRowHasNoProviderIds()
    {
        // Plex scrobbles and MAL adds carry no TVDB/series id — the title match must still land
        // on the show page, never on Sonarr's add-search page.
        var (service, _) = CreateWithProviders(
            tvdbId: null,
            seriesJson: """{"id":42,"title":"Sousou no Frieren","titleSlug":"sousou-no-frieren","tvdbId":403884}""");

        var feed = await service.GetFeedAsync(null, 1, 25);

        var sonarr = Assert.Single(feed.Items[0].ProviderLinks, l => l.Provider == "sonarr");
        Assert.Equal("http://sonarr.local:8989/series/sousou-no-frieren", sonarr.ExternalUrl);
    }

    [Fact]
    public async Task GetFeedAsync_PlexLink_OpensAppPlexTv_NeverTheConfiguredLanHost()
    {
        // http://192.168.x.x:32400 only resolves on that LAN and that browser session;
        // app.plex.tv resolves the server by machine id from anywhere.
        var (service, _) = CreateWithProviders(
            tvdbId: 403884,
            seriesJson: """{"id":42,"title":"Sousou no Frieren","titleSlug":"sousou-no-frieren","tvdbId":403884}""");

        var feed = await service.GetFeedAsync(null, 1, 25);

        var plex = Assert.Single(feed.Items[0].ProviderLinks, l => l.Provider == "plex");
        Assert.StartsWith("https://app.plex.tv/desktop/#!/server/abc123machine/details?key=%2Flibrary%2Fmetadata%2F9001", plex.ExternalUrl);
        Assert.DoesNotContain("192.168.1.20", plex.ExternalUrl);
    }

    [Fact]
    public async Task GetFeedAsync_EveryMediaCard_OffersMalSonarrAndPlexRegardlessOfType()
    {
        // A Sonarr import used to show only the Sonarr button; the card should expose the whole
        // provider set (MAL id resolved by title, since the row itself has no MAL id).
        var (service, _) = CreateWithProviders(
            tvdbId: 403884,
            seriesJson: """{"id":42,"title":"Sousou no Frieren","titleSlug":"sousou-no-frieren","tvdbId":403884}""",
            rowType: ActivityType.EpisodeDownloaded);

        var feed = await service.GetFeedAsync(null, 1, 25);

        var providers = feed.Items[0].ProviderLinks.Select(l => l.Provider).ToList();
        Assert.Equal(new[] { "mal", "sonarr", "plex" }, providers);

        var mal = feed.Items[0].ProviderLinks.Single(l => l.Provider == "mal");
        Assert.Equal("https://myanimelist.net/anime/52991", mal.ExternalUrl);
    }

    [Fact]
    public async Task GetFeedAsync_DropsMediaRowsThatCannotBeTiedToAnAnime()
    {
        // Anime-only hub: a Plex/Sonarr item with no Kometa/MAL match (e.g. Miraculous) must not
        // appear at all, not even with its provider buttons.
        var (service, _) = CreateWithProviders(
            tvdbId: 403884,
            seriesJson: """{"id":42,"title":"Some Other Show","titleSlug":"other","tvdbId":403884}""",
            rowTitle: "Totally Unknown Show");

        var feed = await service.GetFeedAsync(null, 1, 25);

        Assert.Empty(feed.Items);
    }

    [Fact]
    public async Task GetFeedAsync_ResolvesPosterAndMalId_WhenTheRowWasWrittenWithoutThem()
    {
        // Sonarr imports and Plex scrobbles are stored without a poster or a MAL id, which is
        // why those cards used to render as bare boxes. Both are resolved from the caches.
        var (service, _) = CreateWithProviders(
            tvdbId: 403884,
            seriesJson: """{"id":42,"title":"Sousou no Frieren","titleSlug":"sousou-no-frieren","tvdbId":403884,"remotePoster":"https://image.tmdb.org/t/p/original/poster.jpg"}""",
            rowType: ActivityType.EpisodeDownloaded);

        var feed = await service.GetFeedAsync(null, 1, 25);

        // TMDB originals are 1000px wide; the card is 88px, so ask for the 300px variant.
        Assert.Equal("https://image.tmdb.org/t/p/w300/poster.jpg", feed.Items[0].PosterUrl);
        Assert.Equal(52991, feed.Items[0].MalId);
    }

    [Fact]
    public async Task GetFeedAsync_ResolvesMalId_FromTheSonarrMapping_NotJustTheTitle()
    {
        // Plex/Sonarr rows only carry a series id. The anime library already knows which MAL id
        // that series maps to — far more reliable than matching "Always a Catch!" by title.
        var animeGuid = Guid.NewGuid();
        var mappings = new Mock<IAnimeMappingRepository>();
        mappings.Setup(m => m.GetAllAsync(It.IsAny<bool>())).ReturnsAsync(new[]
        {
            new AnimeMapping { AniDbId = "1234", Anime = new Anime { Id = animeGuid, SourceId = "52991", AnimeSource = AnimeSource.MyAnimeList, Title = "Always a Catch!" } },
            new AnimeMapping { AniDbId = "1234", Anime = new Anime { Id = animeGuid, SourceId = "42", AnimeSource = AnimeSource.Sonarr, Title = "Always a Catch!" } },
        });

        var (service, _) = CreateWithProviders(
            tvdbId: 403884,
            seriesJson: """{"id":42,"title":"Always a Catch!","titleSlug":"always-a-catch","tvdbId":403884}""",
            rowType: ActivityType.EpisodeWatched,
            rowTitle: "Always a Catch!",
            mappings: mappings);

        var feed = await service.GetFeedAsync(null, 1, 25);

        Assert.Equal(52991, feed.Items[0].MalId);
        Assert.True(feed.Items[0].ProviderLinks.Single(l => l.Provider == "mal").ExternalUrl
            .EndsWith("/anime/52991"));

        // The internal id is what lets the drawer open for rows with no MAL id of their own.
        Assert.Equal(animeGuid, feed.Items[0].AnimeId);
    }

    [Fact]
    public async Task GetTrendAsync_BucketsEverySeriesIntoTheSelectedPeriod()
    {
        var repo = new Mock<IActivityLogRepository>();
        repo.Setup(r => r.GetSinceAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ActivityLog>
            {
                new() { Type = ActivityType.EpisodeDownloaded, Title = "a", Timestamp = DateTimeOffset.UtcNow.AddHours(-1) },
                new() { Type = ActivityType.AnimeAdded, Title = "b", Timestamp = DateTimeOffset.UtcNow.AddHours(-2) },
                new() { Type = ActivityType.EpisodeWatched, Title = "c", Timestamp = DateTimeOffset.UtcNow.AddHours(-3) },
                new() { Type = ActivityType.SyncCompleted, Title = "d", Timestamp = DateTimeOffset.UtcNow.AddHours(-4) },
            });

        var trend = await Create(repo).GetTrendAsync(ActivityStatsPeriod.ThisWeek, 0);

        Assert.Equal(7, trend.Points.Count);
        Assert.Equal(1, trend.Points.Sum(p => p.Ingested));
        Assert.Equal(1, trend.Points.Sum(p => p.Added));
        Assert.Equal(1, trend.Points.Sum(p => p.Watched));
        Assert.Equal(1, trend.Points.Sum(p => p.Syncs));
    }

    private static (ActivityLogService Service, Mock<IActivityLogRepository> Repo) CreateWithProviders(
        int? tvdbId, string seriesJson, ActivityType rowType = ActivityType.AnimeAdded,
        string rowTitle = "Sousou no Frieren", Mock<IAnimeMappingRepository>? mappings = null)
    {
        var repo = new Mock<IActivityLogRepository>();
        repo.Setup(r => r.CountAsync(It.IsAny<IReadOnlyCollection<ActivityType>?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
        repo.Setup(r => r.GetRecentAsync(It.IsAny<IReadOnlyCollection<ActivityType>?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ActivityLog>
            {
                new() { Type = rowType, Title = rowTitle, TvdbId = tvdbId, Timestamp = DateTimeOffset.UtcNow },
            });

        var propertyBag = new Mock<IPropertyBagRepository>();
        propertyBag.Setup(p => p.GetByKeyAsync(SettingsKeys.SonarrHost))
            .ReturnsAsync(new PropertyBag { Key = SettingsKeys.SonarrHost, Value = "sonarr.local:8989" });
        propertyBag.Setup(p => p.GetByKeyAsync(SettingsKeys.PlexHost))
            .ReturnsAsync(new PropertyBag { Key = SettingsKeys.PlexHost, Value = "http://192.168.1.20:32400" });
        propertyBag.Setup(p => p.GetByKeyAsync(SettingsKeys.PlexMachineIdentifier))
            .ReturnsAsync(new PropertyBag { Key = SettingsKeys.PlexMachineIdentifier, Value = "abc123machine" });

        var cache = new Mock<ICacheEntryRepository>();
        cache.Setup(c => c.GetAsync(AnimeSource.Sonarr))
            .ReturnsAsync(new List<CacheEntry>
            {
                new() { SourceId = "42", RawData = seriesJson, AnimeSource = AnimeSource.Sonarr },
            });
        cache.Setup(c => c.GetAsync(AnimeSource.MyAnimeList))
            .ReturnsAsync(new List<CacheEntry>
            {
                new()
                {
                    SourceId = "52991",
                    RawData = """{"id":52991,"title":"Sousou no Frieren"}""",
                    AnimeSource = AnimeSource.MyAnimeList,
                },
            });
        cache.Setup(c => c.GetAsync(AnimeSource.Plex))
            .ReturnsAsync(new List<CacheEntry>
            {
                new()
                {
                    SourceId = "9001",
                    RawData = """{"ratingKey":"9001","grandparentTitle":"Sousou no Frieren"}""",
                    AnimeSource = AnimeSource.Plex,
                },
            });

        var service = new ActivityLogService(
            repo.Object, propertyBag.Object, cache.Object,
            mappings?.Object ?? Mock.Of<IAnimeMappingRepository>(),
            Mock.Of<ISonarrHistoryEventRepository>(),
            NullLogger<ActivityLogService>.Instance);
        return (service, repo);
    }
}
