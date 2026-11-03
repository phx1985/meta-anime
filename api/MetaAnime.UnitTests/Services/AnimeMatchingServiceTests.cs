using FluentAssertions;
using MetaAnime.Application.Services;
using MetaAnime.Core;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Interfaces.Shared.Results;
using MetaAnime.Core.Models;
using MetaAnime.Domain.Entities;
using MetaAnime.Domain.Enums;
using MetaAnime.Infrastructure.Kometa.Models;
using Microsoft.Extensions.Logging;
using Moq;
using System.Diagnostics;
using Anime = MetaAnime.Domain.Entities.Anime;
using AnimeMapping = MetaAnime.Domain.Entities.AnimeMapping;
using CacheEntry = MetaAnime.Domain.Entities.CacheEntry;
using ExternalAnimeIdentifier = MetaAnime.Domain.Entities.ExternalAnimeIdentifier;
using KometaAnimeMapping = MetaAnime.Domain.Entities.KometaAnimeMapping;
using MalModel = MetaAnime.Infrastructure.MyAnimeList.Models.MalAnime;
using AltTitles = MetaAnime.Infrastructure.MyAnimeList.Models.AlternativeTitles;
using PlexModel = MetaAnime.Infrastructure.Plex.Models.PlexMedia;
using SonarrModel = MetaAnime.Infrastructure.SonarrAPIs.Sonarr.Models.SonarrSeries;
using Statistics = MetaAnime.Infrastructure.SonarrAPIs.Sonarr.Models.Statistics;

namespace MetaAnime.UnitTests.Services;

public class AnimeMatchingServiceTests
{
    private readonly Mock<IKometaAnimeJsonService<KometaRaw>> _kometaJson = new();
    private readonly Mock<IKometaAnimeMappingService> _kometaMappings = new();
    private readonly Mock<IAnimeMappingService> _mappingService = new();
    private readonly Mock<IAnimeService> _animeService = new();
    private readonly Mock<ICacheEntryService> _cacheService = new();
    private readonly Mock<ICrossLinkService> _crossLinks = new();
    private readonly Mock<IPropertyBagRepository> _propertyBag = new();
    private readonly List<AnimeMapping> _persistedMappings = [];

    private readonly AnimeMatchingService _sut;

    public AnimeMatchingServiceTests()
    {
        // Default: mapping table populated, remote unchanged → no Kometa writes.
        _kometaMappings.Setup(s => s.AnyAsync()).ReturnsAsync(Result.Success(true));
        _propertyBag
            .Setup(p => p.GetByKeyAsync(SettingsKeys.KometaAnimeIdsJsonEtag))
            .ReturnsAsync(new PropertyBag { Key = SettingsKeys.KometaAnimeIdsJsonEtag, Value = "etag-1" });
        _kometaJson
            .Setup(s => s.GetIfModifiedAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool>()))
            .ReturnsAsync(Result.Success(new KometaFetchOutcome<KometaRaw>(true, null, "etag-1", null)));
        _kometaMappings
            .Setup(s => s.MergeAsync(It.IsAny<IEnumerable<KometaAnimeMapping>>()))
            .ReturnsAsync(Result.Success());
        _kometaMappings
            .Setup(s => s.GetAllAsync())
            .ReturnsAsync(Result.Success<IList<KometaAnimeMapping>>([]));

        _animeService
            .Setup(s => s.RefreshData(It.IsAny<ISyncProgressNotifier?>()))
            .ReturnsAsync(Result.Success(Enumerable.Empty<Anime>()));
        _cacheService
            .Setup(s => s.GetAllAsync())
            .ReturnsAsync(Result.Success(Enumerable.Empty<CacheEntry>()));
        _crossLinks.Setup(s => s.GetAllAsync()).ReturnsAsync([]);
        _propertyBag.Setup(p => p.UpsertAsync(It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);

        _mappingService
            .Setup(s => s.MergeAsync(It.IsAny<IEnumerable<AnimeMapping>>()))
            .Callback<IEnumerable<AnimeMapping>>(m => _persistedMappings.AddRange(m))
            .ReturnsAsync(Result.Success());

        _sut = new AnimeMatchingService(
            _kometaJson.Object,
            _kometaMappings.Object,
            _mappingService.Object,
            _animeService.Object,
            _cacheService.Object,
            _crossLinks.Object,
            _propertyBag.Object,
            Mock.Of<ILogger<AnimeMatchingService>>());
    }

    private void SetupAnimes(params Anime[] animes)
        => _animeService
            .Setup(s => s.RefreshData(It.IsAny<ISyncProgressNotifier?>()))
            .ReturnsAsync(Result.Success(animes.ToList().AsEnumerable()));

    private void SetupKometaRows(params KometaAnimeMapping[] rows)
        => _kometaMappings.Setup(s => s.GetAllAsync()).ReturnsAsync(Result.Success<IList<KometaAnimeMapping>>(rows.ToList()));

    private static Anime MalAnime(string sourceId, string title) => new()
    {
        Id = Guid.CreateVersion7(),
        SourceId = sourceId,
        Title = title,
        AnimeSource = AnimeSource.MyAnimeList,
    };

    private static Anime SonarrAnime(string sourceId, string title, string? tvdbId = null) => new()
    {
        Id = Guid.CreateVersion7(),
        SourceId = sourceId,
        Title = title,
        AnimeSource = AnimeSource.Sonarr,
        ExternalAnimeIdentifiers = tvdbId is null
            ? []
            : [new ExternalAnimeIdentifier { MediaPlatform = MediaPlatform.Tvdb, MediaPlatformId = tvdbId }],
    };

    private static Anime PlexAnime(string sourceId, string title, string? tvdbId = null, string? tmdbId = null) => new()
    {
        Id = Guid.CreateVersion7(),
        SourceId = sourceId,
        Title = title,
        AnimeSource = AnimeSource.Plex,
        ExternalAnimeIdentifiers =
        [
            .. new[]
            {
                tvdbId is null ? null : new ExternalAnimeIdentifier { MediaPlatform = MediaPlatform.Tvdb, MediaPlatformId = tvdbId },
                tmdbId is null ? null : new ExternalAnimeIdentifier { MediaPlatform = MediaPlatform.Tmdb, MediaPlatformId = tmdbId },
            }.Where(i => i is not null)!,
        ],
    };

    private static KometaAnimeMapping Row(string aniDbId, string platformName, string platformId, int? season = null)
        => new() { AniDbId = aniDbId, PlatformName = platformName, PlatformId = platformId, Season = season };

    private static CacheEntry CacheFor(Anime anime, string rawData) => new()
    {
        SourceId = anime.SourceId,
        AnimeSource = anime.AnimeSource,
        RawData = rawData,
    };

    [Fact]
    public async Task MatchAsync_ExactIdMatches_ArePersistedAsExactTier()
    {
        var mal = MalAnime("100", "Foo");
        var sonarr = SonarrAnime("7", "Foo", tvdbId: "55");
        SetupAnimes(mal, sonarr);
        SetupKometaRows(
            Row("1", "mal_id", "100"),
            Row("1", "tvdb_id", "55"));

        var result = await _sut.MatchAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.ExactIdMappings.Should().Be(2);
        result.Value.StrongTitleYearMappings.Should().Be(0);
        _persistedMappings.Should().HaveCount(2);
        _persistedMappings.Should().Contain(m => m.AnimeId == mal.Id && m.AniDbId == "1");
        _persistedMappings.Should().Contain(m => m.AnimeId == sonarr.Id && m.AniDbId == "1");
    }

    [Fact]
    public async Task MatchAsync_UnchangedRemote_SkipsKometaRebuildEntirely()
    {
        SetupAnimes();

        var result = await _sut.MatchAsync();

        result.IsSuccess.Should().BeTrue();
        _kometaMappings.Verify(
            s => s.MergeAsync(It.IsAny<IEnumerable<KometaAnimeMapping>>()),
            Times.Never);
        _kometaMappings.Verify(s => s.BulkDeleteAllAsync(), Times.Never);
    }

    [Fact]
    public async Task MatchAsync_ForceRefresh_ReDownloadsAndMergesWithoutWipe()
    {
        SetupAnimes();
        _kometaJson
            .Setup(s => s.GetIfModifiedAsync(It.IsAny<string?>(), It.IsAny<string?>(), true))
            .ReturnsAsync(Result.Success(new KometaFetchOutcome<KometaRaw>(
                false,
                [new KometaRaw { AniDbId = "1", MalIds = ["100"] }],
                "etag-2",
                null)));

        var result = await _sut.MatchAsync(forceRefreshKometaMapping: true);

        result.IsSuccess.Should().BeTrue();
        _kometaJson.Verify(s => s.GetIfModifiedAsync(It.IsAny<string?>(), It.IsAny<string?>(), true), Times.Once);
        _kometaMappings.Verify(s => s.MergeAsync(It.IsAny<IEnumerable<KometaAnimeMapping>>()), Times.Once);
        _kometaMappings.Verify(s => s.BulkDeleteAllAsync(), Times.Never);
    }

    [Fact]
    public async Task MatchAsync_FuzzyBridge_TitlePlusYear_PersistsBorrowedAniDbId()
    {
        var sonarr = SonarrAnime("7", "Frieren: Beyond Journey's End", tvdbId: "55");
        var mal = MalAnime("100", "Frieren Beyond Journey's End");
        SetupAnimes(sonarr, mal);
        SetupKometaRows(Row("1", "tvdb_id", "55"));
        _cacheService.Setup(s => s.GetAllAsync()).ReturnsAsync(Result.Success(Enumerable.Empty<CacheEntry>()
            .Append(CacheFor(sonarr, """{"year":2023}"""))
            .Append(CacheFor(mal, """{"start_date":"2023-09-29","num_episodes":28}"""))));

        var result = await _sut.MatchAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.ExactIdMappings.Should().Be(1);            // Sonarr via tvdb
        result.Value.StrongTitleYearMappings.Should().Be(1);    // MAL bridged by title+year
        _persistedMappings.Should().Contain(m => m.AnimeId == mal.Id && m.AniDbId == "1");
    }

    [Fact]
    public async Task MatchAsync_FranchiseSequel_NotBridgedWithoutSeasonEvidence()
    {
        var sonarr = SonarrAnime("7", "Demon Slayer", tvdbId: "55");
        var mal = MalAnime("100", "Demon Slayer Season 2");
        SetupAnimes(sonarr, mal);
        SetupKometaRows(Row("1", "tvdb_id", "55")); // no tvdb_season split
        _cacheService.Setup(s => s.GetAllAsync()).ReturnsAsync(Result.Success(Enumerable.Empty<CacheEntry>()
            .Append(CacheFor(sonarr, """{"year":2019}"""))
            .Append(CacheFor(mal, """{"start_date":"2019-04-06","num_episodes":26}"""))));

        var result = await _sut.MatchAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.StrongTitleYearMappings.Should().Be(0);
        _persistedMappings.Should().NotContain(m => m.AnimeId == mal.Id);
        result.Value.UnmatchedCandidates.Should().ContainSingle(u => u.SourceId == "100");
    }

    [Fact]
    public async Task MatchAsync_TvdbSeasonSplit_BridgesOnlyTheMatchingSeasonRow()
    {
        var sonarr = SonarrAnime("7", "One Piece", tvdbId: "55");
        var mal = MalAnime("100", "One Piece Season 2");
        SetupAnimes(sonarr, mal);
        SetupKometaRows(
            Row("1", "tvdb_id", "55", season: 1),
            Row("2", "tvdb_id", "55", season: 2));
        _cacheService.Setup(s => s.GetAllAsync()).ReturnsAsync(Result.Success(Enumerable.Empty<CacheEntry>()
            .Append(CacheFor(sonarr, """{"year":1999}"""))
            .Append(CacheFor(mal, """{"start_date":"1999-10-20","num_episodes":1000}"""))));

        var result = await _sut.MatchAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.StrongTitleYearMappings.Should().Be(1);
        _persistedMappings.Should().ContainSingle(m => m.AnimeId == mal.Id);
        _persistedMappings.Should().Contain(m => m.AnimeId == mal.Id && m.AniDbId == "2");
        _persistedMappings.Should().NotContain(m => m.AnimeId == mal.Id && m.AniDbId == "1");
    }

    [Fact]
    public async Task MatchAsync_CrossLink_CoveredByExactId_IsPruned()
    {
        var mal = MalAnime("100", "Foo");
        SetupAnimes(mal);
        SetupKometaRows(Row("9", "mal_id", "100"));
        _crossLinks.Setup(s => s.GetAllAsync()).ReturnsAsync([new CrossLinkEntry("9", "100")]);
        _crossLinks.Setup(s => s.RemoveAsync("9", "100")).Returns(Task.CompletedTask);

        var result = await _sut.MatchAsync();

        result.IsSuccess.Should().BeTrue();
        _crossLinks.Verify(s => s.RemoveAsync("9", "100"), Times.Once);
    }

    [Fact]
    public async Task MatchAsync_CrossLink_OnlyCoveredByFuzzyBridge_Survives()
    {
        var sonarr = SonarrAnime("7", "Foo", tvdbId: "55");
        var mal = MalAnime("100", "Foo");
        SetupAnimes(sonarr, mal);
        SetupKometaRows(Row("9", "tvdb_id", "55"));
        _cacheService.Setup(s => s.GetAllAsync()).ReturnsAsync(Result.Success(Enumerable.Empty<CacheEntry>()
            .Append(CacheFor(sonarr, """{"year":2020}"""))
            .Append(CacheFor(mal, """{"start_date":"2020-01-01","num_episodes":12}"""))));
        _crossLinks.Setup(s => s.GetAllAsync()).ReturnsAsync([new CrossLinkEntry("9", "100")]);

        var result = await _sut.MatchAsync();

        result.IsSuccess.Should().BeTrue();
        // The manual override is neither pruned nor duplicated.
        _crossLinks.Verify(s => s.RemoveAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _persistedMappings.Count(m => m.AnimeId == mal.Id && m.AniDbId == "9").Should().Be(1);
    }

    [Fact]
    public async Task MatchAsync_TitleOnlyMatch_IsNotAutoPersisted_ButReported()
    {
        var sonarr = SonarrAnime("7", "Foo", tvdbId: "55");
        var mal = MalAnime("100", "Foo");
        SetupAnimes(sonarr, mal);
        SetupKometaRows(Row("1", "tvdb_id", "55"));
        _cacheService.Setup(s => s.GetAllAsync()).ReturnsAsync(Result.Success(Enumerable.Empty<CacheEntry>()
            .Append(CacheFor(sonarr, """{"year":2015}"""))
            .Append(CacheFor(mal, """{"start_date":"2021-01-01","num_episodes":12}"""))));

        var result = await _sut.MatchAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.StrongTitleYearMappings.Should().Be(0);
        _persistedMappings.Should().NotContain(m => m.AnimeId == mal.Id);
        var unmatched = result.Value.UnmatchedCandidates.Should().ContainSingle(u => u.SourceId == "100").Subject;
        unmatched.Candidates.Should().ContainSingle(c => c.Confidence == MatchConfidence.TitleOnly);
    }

    [Fact]
    public async Task MatchAsync_TvdbMiss_FallsThroughToLowerPriorityPlatform()
    {
        var sonarr = SonarrAnime("7", "Foo");
        sonarr.ExternalAnimeIdentifiers =
        [
            new ExternalAnimeIdentifier { MediaPlatform = MediaPlatform.Tvdb, MediaPlatformId = "55" },
            new ExternalAnimeIdentifier { MediaPlatform = MediaPlatform.Imdb, MediaPlatformId = "tt1" },
        ];
        SetupAnimes(sonarr);
        // tvdb 55 has no Kometa row, but imdb tt1 does → fall-through must find it.
        SetupKometaRows(Row("1", "imdb_id", "tt1"));

        var result = await _sut.MatchAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.ExactIdMappings.Should().Be(1);
        _persistedMappings.Should().ContainSingle(m => m.AnimeId == sonarr.Id && m.AniDbId == "1");
    }

    // ── Regression: real-data failure shapes (2026-11-01) ────────────────────────

    [Fact]
    public async Task MatchAsync_ExactTitle_WithMissingYearOnOneSide_PersistsBridge()
    {
        // Real shape: new Sonarr series without a year yet ("Magical Buffs…") vs a MAL row
        // whose romaji main title carries the English title as an alternative title.
        // Year/episode counts are tie-breakers — a missing year must never demote this.
        var sonarr = SonarrAnime("950", "Magical Buffs: The Support Caster is Stronger Than He Realized!", tvdbId: "55");
        var mal = MalAnime("62535", "Zatsuyou Fuyo Jutsushi ga Jibun no Saikyou ni Kizuku made");
        SetupAnimes(sonarr, mal);
        SetupKometaRows(Row("1", "tvdb_id", "55"));
        _cacheService.Setup(s => s.GetAllAsync()).ReturnsAsync(Result.Success(Enumerable.Empty<CacheEntry>()
            .Append(new SonarrModel { Id = 950, Title = sonarr.Title, CleanTitle = "magicalbuffssupportcasterisstrongerthanherealized" }.ToCacheEntry())
            .Append(new MalModel
            {
                Id = 62535,
                Title = mal.Title,
                AlternativeTitles = new AltTitles
                {
                    En = "Magical Buffs: The Support Caster Is Stronger Than He Realized!",
                    Ja = "雑用付与術師が自分の最強に気づくまで",
                    Synonyms = [],
                },
                StartDate = "2025-10-05",
                NumEpisodes = 12,
            }.ToCacheEntry())));

        var result = await _sut.MatchAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.StrongTitleYearMappings.Should().Be(1);
        _persistedMappings.Should().ContainSingle(m => m.AnimeId == mal.Id && m.AniDbId == "1");
    }

    [Fact]
    public async Task MatchAsync_TiedSameShowCandidates_AcrossPlatforms_PersistSharedAniDbId()
    {
        // Real shape: "Hanaori-san…" exists in Sonarr AND Plex (same show, same AniDB
        // identity) and its MAL row carries the English title as an alternative title.
        // The two candidates tie exactly — that is the same show twice, not an ambiguity.
        var sonarr = SonarrAnime("949", "Hanaori-san Still Wants to Fight in the Next Life", tvdbId: "55");
        var plex = PlexAnime("73186", "Hanaori-san Still Wants to Fight in the Next Life", tvdbId: "55");
        var mal = MalAnime("62535", "Hanaori-san wa Tensei shitemo Kenka ga Shitai");
        SetupAnimes(sonarr, plex, mal);
        SetupKometaRows(Row("1", "tvdb_id", "55"));
        _cacheService.Setup(s => s.GetAllAsync()).ReturnsAsync(Result.Success(Enumerable.Empty<CacheEntry>()
            .Append(new SonarrModel
            {
                Id = 949,
                Title = sonarr.Title,
                Year = 2026,
                Statistics = new Statistics { EpisodeCount = 12, TotalEpisodeCount = 12 },
            }.ToCacheEntry())
            .Append(new PlexModel
            {
                Description = "",
                Id = "73186",
                Title = plex.Title,
                ExternalGuilds = ["tvdb://55"],
                Year = 2026,
                LeafCount = 12,
            }.ToCacheEntry())
            .Append(new MalModel
            {
                Id = 62535,
                Title = mal.Title,
                AlternativeTitles = new AltTitles { En = "Hanaori-san Still Wants to Fight in the Next Life" },
                StartDate = "2026-01-05",
                NumEpisodes = 12,
            }.ToCacheEntry())));

        var result = await _sut.MatchAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.StrongTitleYearMappings.Should().Be(1);
        _persistedMappings.Should().ContainSingle(m => m.AnimeId == mal.Id && m.AniDbId == "1");
    }

    [Fact]
    public async Task MatchAsync_TiedDistinctShows_WithDisjointAniDbIds_AreNotAutoPersisted()
    {
        // Same title, no year/episode data on either side, but the candidates resolve to
        // DIFFERENT AniDB identities (e.g. a remake) — nothing may be auto-persisted.
        var sonarr = SonarrAnime("1", "Foo", tvdbId: "55");
        var plex = PlexAnime("2", "Foo", tmdbId: "77");
        var mal = MalAnime("100", "Foo");
        SetupAnimes(sonarr, plex, mal);
        SetupKometaRows(Row("1", "tvdb_id", "55"), Row("2", "tmdb_show_id", "77"));
        _cacheService.Setup(s => s.GetAllAsync()).ReturnsAsync(Result.Success(Enumerable.Empty<CacheEntry>()
            .Append(new SonarrModel { Id = 1, Title = sonarr.Title }.ToCacheEntry())
            .Append(new PlexModel { Description = "", Id = "2", Title = plex.Title, ExternalGuilds = ["tmdb://77"] }.ToCacheEntry())
            .Append(new MalModel { Id = 100, Title = mal.Title }.ToCacheEntry())));

        var result = await _sut.MatchAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.StrongTitleYearMappings.Should().Be(0);
        _persistedMappings.Should().NotContain(m => m.AnimeId == mal.Id);
        result.Value.UnmatchedCandidates.Should().ContainSingle(u => u.SourceId == "100");
    }

    [Fact]
    public async Task MatchAsync_YearConflict_DemotesEvenExactTitle()
    {
        // Remake guard: identical titles but years 6 apart must not bridge.
        var sonarr = SonarrAnime("1", "Foo", tvdbId: "55");
        var mal = MalAnime("100", "Foo");
        SetupAnimes(sonarr, mal);
        SetupKometaRows(Row("1", "tvdb_id", "55"));
        _cacheService.Setup(s => s.GetAllAsync()).ReturnsAsync(Result.Success(Enumerable.Empty<CacheEntry>()
            .Append(new SonarrModel { Id = 1, Title = sonarr.Title, Year = 2001 }.ToCacheEntry())
            .Append(new MalModel { Id = 100, Title = mal.Title, StartDate = "2019-01-01" }.ToCacheEntry())));

        var result = await _sut.MatchAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.StrongTitleYearMappings.Should().Be(0);
        _persistedMappings.Should().NotContain(m => m.AnimeId == mal.Id);
        result.Value.UnmatchedCandidates.Should().ContainSingle(u => u.SourceId == "100");
    }

    [Fact]
    public async Task MatchAsync_LargeLibrary_StaysWithinTimeBudget()
    {
        // Guards against re-introducing the O(unresolved × resolved × words² × Levenshtein)
        // scan: with random-vocabulary titles almost every pair fails the cheap word-overlap
        // gate, so the whole match stays well under a second — the ungated scan takes tens.
        var rnd = new Random(42);
        string[] vocab = [.. Enumerable.Range(0, 160).Select(i => $"vok{i}")];
        string LongTitle() => string.Join(' ', Enumerable.Range(0, 7).Select(_ => vocab[rnd.Next(vocab.Length)]));

        var kometaRows = new List<KometaAnimeMapping>();
        var animes = new List<Anime>();
        var caches = new List<CacheEntry>();

        for (var i = 0; i < 1000; i++)
        {
            var sonarr = SonarrAnime($"s{i}", LongTitle(), tvdbId: $"tv{i}");
            animes.Add(sonarr);
            kometaRows.Add(Row($"a{i}", "tvdb_id", $"tv{i}"));
            caches.Add(new SonarrModel { Id = i, Title = sonarr.Title, Year = 2020 }.ToCacheEntry());
        }

        for (var i = 0; i < 300; i++)
        {
            var mal = MalAnime($"m{i}", LongTitle());
            animes.Add(mal);
            caches.Add(new MalModel { Id = i, Title = mal.Title, StartDate = "2020-01-01", NumEpisodes = 12 }.ToCacheEntry());
        }

        SetupAnimes(animes.ToArray());
        SetupKometaRows(kometaRows.ToArray());
        _cacheService.Setup(s => s.GetAllAsync()).ReturnsAsync(Result.Success(caches.AsEnumerable()));

        var sw = Stopwatch.StartNew();
        var result = await _sut.MatchAsync();
        sw.Stop();

        result.IsSuccess.Should().BeTrue();
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(8));
    }
}
