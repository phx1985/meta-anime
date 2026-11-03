using FluentAssertions;
using MetaAnime.Application;
using MetaAnime.Application.Services;
using MetaAnime.Application.Services.AniList;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Domain.Entities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace MetaAnime.UnitTests.Services;

public class AniListCastServiceTests
{
    private readonly Mock<IAnimeCastRepository> _castRepository = new();
    private readonly Mock<IVoiceActorRepository> _vaRepository = new();
    private readonly Mock<IPropertyBagRepository> _propertyBag = new();
    private readonly Mock<IAniListClient> _client = new();
    private readonly AniListCastNegativeCache _negativeCache = new(new MemoryCache(new MemoryCacheOptions()));
    private readonly CastIdSpaceMigrator _migrator;
    private readonly AniListCastService _sut;

    public AniListCastServiceTests()
    {
        _migrator = new CastIdSpaceMigrator(
            _castRepository.Object, _vaRepository.Object, _propertyBag.Object, NullLogger<CastIdSpaceMigrator>.Instance);

        // Default: the ID-space migration already ran — services under test must not re-clear rows.
        _propertyBag.Setup(p => p.GetByKeyAsync(CastIdSpaceMigrator.MarkerKey))
                    .ReturnsAsync(NewMarker(CastIdSpaceMigrator.AniListIdSpace));
        _propertyBag.Setup(p => p.UpsertAsync(It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);
        _castRepository.Setup(r => r.DeleteAllAsync()).Returns(Task.CompletedTask);
        _vaRepository.Setup(r => r.DeleteAllAsync()).Returns(Task.CompletedTask);

        _sut = new AniListCastService(_castRepository.Object, _client.Object, _negativeCache, _migrator, NullLogger<AniListCastService>.Instance);
        _castRepository.Setup(r => r.GetByAnimeMalIdAsync(It.IsAny<string>())).ReturnsAsync([]);
        _castRepository.Setup(r => r.BulkUpsertAsync(It.IsAny<IEnumerable<AnimeCast>>())).Returns(Task.CompletedTask);
    }

    private static PropertyBag NewMarker(string value) => new()
    {
        Key = CastIdSpaceMigrator.MarkerKey,
        Value = value,
        CreationDate = DateTime.UtcNow,
        UpdateDate = DateTime.UtcNow,
    };

    private void SetupFetch(AniListApiResult<AniListMediaCastData> result) =>
        _client.Setup(c => c.PostQueryAsync<AniListMediaCastData>(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, object?>>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(result);

    /// <summary>Scripts one response per cast page and records the requested page numbers.</summary>
    private List<int> SetupPages(params AniListApiResult<AniListMediaCastData>[] results)
    {
        var requestedPages = new List<int>();
        var queue = new Queue<AniListApiResult<AniListMediaCastData>>(results);
        _client.Setup(c => c.PostQueryAsync<AniListMediaCastData>(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, object?>>(), It.IsAny<CancellationToken>()))
               .Callback<string, IReadOnlyDictionary<string, object?>?, CancellationToken>((_, vars, _) =>
                   requestedPages.Add((int)vars!["page"]!))
               .Returns(() => Task.FromResult(queue.Dequeue()));
        return requestedPages;
    }

    private static AnimeCast CastRow(string malId) => new()
    {
        AnimeMalId = malId,
        CharacterId = 1,
        CharacterName = "Taiga",
        CharacterRole = "Main",
        VaPersonId = 2,
        VaName = "Rie",
        Language = "Japanese",
        SyncedAt = DateTime.UtcNow,
    };

    private static AniListApiResult<AniListMediaCastData> Page(bool hasNextPage, params AniListCharacterEdge[] edges) =>
        AniListApiResult<AniListMediaCastData>.Ok(new AniListMediaCastData
        {
            Media = new AniListMedia
            {
                IdMal = 123,
                Title = new AniListTitle { Romaji = "Toradora!" },
                Characters = new AniListCharacterConnection
                {
                    PageInfo = new AniListPageInfo { HasNextPage = hasNextPage },
                    Edges = edges.ToList(),
                },
            },
        });

    private static AniListCharacterEdge Edge(string role, int charId, string charName, params AniListVoiceActor[] vas) => new()
    {
        Role = role,
        Node = new AniListCharacter
        {
            Id = charId,
            Name = new AniListName { Full = charName },
            Image = new AniListImage { Large = $"https://img/char/{charId}.png" },
        },
        VoiceActors = vas.ToList(),
    };

    private static AniListVoiceActor Va(int id, string name, string? language, string? large = null, string? medium = null) => new()
    {
        Id = id,
        Name = new AniListName { Full = name },
        LanguageV2 = language,
        Image = new AniListImage { Large = large, Medium = medium },
    };

    [Fact]
    public async Task ExistingRows_AreServedWithZeroNetwork_EvenDuringOutage()
    {
        _castRepository.Setup(r => r.GetByAnimeMalIdAsync("123")).ReturnsAsync([CastRow("123")]);
        _negativeCache.Set("123", AniListNegativeState.ProviderUnavailable); // stale negative must not win

        var result = await _sut.EnsureAsync("123");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(CastFetchOutcome.AlreadyCached);
        _client.Verify(c => c.PostQueryAsync<AniListMediaCastData>(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, object?>>(), It.IsAny<CancellationToken>()), Times.Never);
        _negativeCache.TryGet("123", out _).Should().BeFalse(); // negative state cleared
    }

    [Fact]
    public async Task NegativeCache_ProviderUnavailable_ReturnsExact503Error_WithZeroNetwork()
    {
        _negativeCache.Set("123", AniListNegativeState.ProviderUnavailable);

        var result = await _sut.EnsureAsync("123");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(Constants.Errors.CastProviderUnavailable);
        result.Error.Message.Should().Be("Cast provider is unavailable for anime 123");
        _client.Verify(c => c.PostQueryAsync<AniListMediaCastData>(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, object?>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NegativeCache_DefinitiveEmpty_ReturnsNoData_WithZeroNetwork()
    {
        _negativeCache.Set("123", AniListNegativeState.DefinitiveEmpty);

        var result = await _sut.EnsureAsync("123");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(CastFetchOutcome.NoData);
        _client.Verify(c => c.PostQueryAsync<AniListMediaCastData>(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, object?>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FetchSuccess_PersistsMappedRows_RolesIdsLanguageAndImages()
    {
        SetupFetch(Page(false,
            Edge("MAIN", 10, "Taiga Aisaka",
                Va(20, "Rie Kugimiya", "Japanese", medium: "https://img/rie.png"),
                Va(21, "Rie Kugimiya", "Japanese"),                     // duplicate character+language → dropped
                Va(22, "English Va", null)),                            // null language → empty string
            Edge("SUPPORTING", 11, "Minori Kushieda", Va(23, "Yui Horie", "Japanese")),
            Edge("BACKGROUND", 12, "Extra", Va(24, "Nobody", "Japanese"))));

        List<AnimeCast>? upserted = null;
        _castRepository.Setup(r => r.BulkUpsertAsync(It.IsAny<IEnumerable<AnimeCast>>()))
                       .Callback<IEnumerable<AnimeCast>>(e => upserted = e.ToList())
                       .Returns(Task.CompletedTask);

        var result = await _sut.EnsureAsync("123");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(CastFetchOutcome.Stored);
        upserted.Should().HaveCount(4);

        upserted!.Select(r => r.CharacterRole).Should().Equal("Main", "Main", "Supporting", "Supporting");

        var taigaRows = upserted.Where(r => r.CharacterId == 10).ToList();
        taigaRows.Should().HaveCount(2); // dup language dropped, null language kept
        taigaRows[0].AnimeMalId.Should().Be("123");
        taigaRows[0].CharacterName.Should().Be("Taiga Aisaka");
        taigaRows[0].CharacterImageUrl.Should().Be("https://img/char/10.png");
        taigaRows[0].VaPersonId.Should().Be(20);
        taigaRows[0].VaName.Should().Be("Rie Kugimiya");
        taigaRows[0].VaImageUrl.Should().Be("https://img/rie.png"); // large missing → medium fallback
        taigaRows[0].Language.Should().Be("Japanese");
        taigaRows[1].VaPersonId.Should().Be(22);
        taigaRows[1].Language.Should().Be(string.Empty);

        _negativeCache.TryGet("123", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Paging_LoopsWhileHasNextPage_AccumulatingAllPages()
    {
        var requestedPages = SetupPages(
            Page(true, Edge("MAIN", 10, "First Page Char", Va(20, "Va A", "Japanese"))),
            Page(false, Edge("SUPPORTING", 11, "Second Page Char", Va(21, "Va B", "Japanese"))));

        List<AnimeCast>? upserted = null;
        _castRepository.Setup(r => r.BulkUpsertAsync(It.IsAny<IEnumerable<AnimeCast>>()))
                       .Callback<IEnumerable<AnimeCast>>(e => upserted = e.ToList())
                       .Returns(Task.CompletedTask);

        var result = await _sut.EnsureAsync("123");

        result.Value.Should().Be(CastFetchOutcome.Stored);
        requestedPages.Should().Equal(1, 2);
        upserted.Should().HaveCount(2);
        upserted!.Select(r => r.CharacterName).Should().Equal("First Page Char", "Second Page Char");
    }

    [Fact]
    public async Task Paging_IsBoundedAtTenPages()
    {
        // Every page claims there is more — the loop must stop at the 10-page bound.
        var requestedPages = SetupPages(Enumerable.Range(0, 10)
            .Select(i => Page(true, Edge("MAIN", 100 + i, $"Char {i}", Va(200 + i, $"Va {i}", "Japanese"))))
            .ToArray());

        var result = await _sut.EnsureAsync("123");

        result.IsSuccess.Should().BeTrue();
        requestedPages.Should().HaveCount(10);
        requestedPages.Should().Equal(Enumerable.Range(1, 10));
    }

    [Fact]
    public async Task UnknownMalId_MediaNull_IsRememberedAsDefinitiveEmpty()
    {
        SetupFetch(AniListApiResult<AniListMediaCastData>.Ok(new AniListMediaCastData { Media = null }));

        (await _sut.EnsureAsync("123")).Value.Should().Be(CastFetchOutcome.NoData);

        // Second call answers from the negative cache — zero network.
        (await _sut.EnsureAsync("123")).Value.Should().Be(CastFetchOutcome.NoData);
        _negativeCache.TryGet("123", out var state).Should().BeTrue();
        state.Should().Be(AniListNegativeState.DefinitiveEmpty);
        _client.Verify(c => c.PostQueryAsync<AniListMediaCastData>(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, object?>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EmptyCast_IsRememberedAsDefinitiveEmpty()
    {
        SetupFetch(Page(false));

        (await _sut.EnsureAsync("123")).Value.Should().Be(CastFetchOutcome.NoData);
        (await _sut.EnsureAsync("123")).Value.Should().Be(CastFetchOutcome.NoData);
        _client.Verify(c => c.PostQueryAsync<AniListMediaCastData>(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, object?>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NonNumericMalId_IsDefinitivelyEmpty_WithZeroNetwork()
    {
        (await _sut.EnsureAsync("not-a-mal-id")).Value.Should().Be(CastFetchOutcome.NoData);
        _client.Verify(c => c.PostQueryAsync<AniListMediaCastData>(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, object?>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProviderOutage_IsRememberedAsProviderUnavailable()
    {
        SetupFetch(AniListApiResult<AniListMediaCastData>.Down("connection refused"));

        var first = await _sut.EnsureAsync("123");
        first.IsFailure.Should().BeTrue();
        first.Error.Code.Should().Be(Constants.Errors.CastProviderUnavailable);
        first.Error.Message.Should().Be("Cast provider is unavailable for anime 123");

        var second = await _sut.EnsureAsync("123");
        second.IsFailure.Should().BeTrue();
        second.Error.Message.Should().Be("Cast provider is unavailable for anime 123");

        // The outage answer is remembered — only one network round-trip total.
        _client.Verify(c => c.PostQueryAsync<AniListMediaCastData>(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, object?>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MidPagingOutage_PersistsNothing_AndFails()
    {
        var requestedPages = SetupPages(
            Page(true, Edge("MAIN", 10, "First Page Char", Va(20, "Va A", "Japanese"))),
            AniListApiResult<AniListMediaCastData>.Down("connection refused"));

        var result = await _sut.EnsureAsync("123");

        // A partial cast must never be stored as if it were complete.
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(Constants.Errors.CastProviderUnavailable);
        requestedPages.Should().Equal(1, 2);
        _castRepository.Verify(r => r.BulkUpsertAsync(It.IsAny<IEnumerable<AnimeCast>>()), Times.Never);
    }

    [Fact]
    public async Task LoadingFlag_IsSetDuringFetch_AndClearedAfter()
    {
        var loadingDuringFetch = false;
        _client.Setup(c => c.PostQueryAsync<AniListMediaCastData>(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, object?>>(), It.IsAny<CancellationToken>()))
               .Returns(() =>
               {
                   loadingDuringFetch = _negativeCache.IsLoading("123");
                   return Task.FromResult(AniListApiResult<AniListMediaCastData>.Ok(new AniListMediaCastData()));
               });

        await _sut.EnsureAsync("123");

        loadingDuringFetch.Should().BeTrue();
        _negativeCache.IsLoading("123").Should().BeFalse();
    }

    [Fact]
    public async Task LoadingFlag_IsClearedOnCacheHitPath()
    {
        _negativeCache.Set("123", AniListNegativeState.DefinitiveEmpty);
        _negativeCache.MarkLoading("123");

        await _sut.EnsureAsync("123");

        _negativeCache.IsLoading("123").Should().BeFalse();
    }

    [Fact]
    public async Task UnmigratedIdSpace_ClearsOldRows_BeforeServingCache()
    {
        _propertyBag.Setup(p => p.GetByKeyAsync(CastIdSpaceMigrator.MarkerKey))
                    .ReturnsAsync((PropertyBag?)null); // pre-AniList rows may be present
        _castRepository.Setup(r => r.GetByAnimeMalIdAsync("123")).ReturnsAsync([CastRow("123")]);
        SetupFetch(Page(false, Edge("MAIN", 10, "Taiga Aisaka", Va(20, "Rie Kugimiya", "Japanese"))));

        await _sut.EnsureAsync("123");

        _castRepository.Verify(r => r.DeleteAllAsync(), Times.Once);
        _vaRepository.Verify(r => r.DeleteAllAsync(), Times.Once);
        _propertyBag.Verify(p => p.UpsertAsync(CastIdSpaceMigrator.MarkerKey, CastIdSpaceMigrator.AniListIdSpace), Times.Once);
    }
}
