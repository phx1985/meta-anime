using FluentAssertions;
using MetaAnime.Application;
using MetaAnime.Application.Services;
using MetaAnime.Application.Services.AniList;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace MetaAnime.UnitTests.Services;

public class AniListPersonServiceTests
{
    private readonly Mock<IVoiceActorRepository> _repository = new();
    private readonly Mock<IAnimeCastRepository> _castRepository = new();
    private readonly Mock<IPropertyBagRepository> _propertyBag = new();
    private readonly Mock<IAniListClient> _client = new();
    private readonly AniListPersonService _sut;

    public AniListPersonServiceTests()
    {
        var migrator = new CastIdSpaceMigrator(
            _castRepository.Object, _repository.Object, _propertyBag.Object, NullLogger<CastIdSpaceMigrator>.Instance);

        // Default: the ID-space migration already ran.
        _propertyBag.Setup(p => p.GetByKeyAsync(CastIdSpaceMigrator.MarkerKey))
                    .ReturnsAsync(new PropertyBag
                    {
                        Key = CastIdSpaceMigrator.MarkerKey,
                        Value = CastIdSpaceMigrator.AniListIdSpace,
                        CreationDate = DateTime.UtcNow,
                        UpdateDate = DateTime.UtcNow,
                    });

        _sut = new AniListPersonService(_repository.Object, _client.Object, migrator, NullLogger<AniListPersonService>.Instance);
        _repository.Setup(r => r.GetByPersonIdAsync(It.IsAny<int>())).ReturnsAsync((VoiceActor?)null);
        _repository.Setup(r => r.UpsertAsync(It.IsAny<VoiceActor>())).Returns(Task.CompletedTask);
    }

    private void SetupFetch(AniListApiResult<AniListStaffData> result) =>
        _client.Setup(c => c.PostQueryAsync<AniListStaffData>(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, object?>>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(result);

    private static AniListApiResult<AniListStaffData> Staff(AniListStaff? staff) =>
        AniListApiResult<AniListStaffData>.Ok(new AniListStaffData { Staff = staff });

    [Fact]
    public async Task FreshCache_IsServedWithZeroNetwork()
    {
        _repository.Setup(r => r.GetByPersonIdAsync(7))
                   .ReturnsAsync(new VoiceActor { PersonId = 7, Name = "Cached", CachedAt = DateTime.UtcNow.AddHours(-1) });

        var result = await _sut.GetOrFetchAsync(7);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Name.Should().Be("Cached");
        _client.Verify(c => c.PostQueryAsync<AniListStaffData>(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, object?>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StaleCache_TriggersFetch_AndUpsertsNormalizedProfile()
    {
        _repository.Setup(r => r.GetByPersonIdAsync(7))
                   .ReturnsAsync(new VoiceActor { PersonId = 7, Name = "Old", CachedAt = DateTime.UtcNow.AddDays(-31) });

        SetupFetch(Staff(new AniListStaff
        {
            Id = 7,
            Name = new AniListName { Full = "Rie Kugimiya", Native = "釘宮理恵" },
            Image = new AniListImage { Medium = "https://img/rie.png" }, // large missing → medium fallback
            DateOfBirth = new AniListFuzzyDate { Year = 1979, Month = 5, Day = 30 },
            BloodType = "AB",
        }));

        VoiceActor? upserted = null;
        _repository.Setup(r => r.UpsertAsync(It.IsAny<VoiceActor>()))
                   .Callback<VoiceActor>(va => upserted = va)
                   .Returns(Task.CompletedTask);

        var result = await _sut.GetOrFetchAsync(7);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Name.Should().Be("Rie Kugimiya");
        result.Value.FamilyName.Should().Be("Kugimiya"); // full is given-then-family → last token is family
        result.Value.GivenName.Should().Be("Rie");
        result.Value.BloodType.Should().Be("AB");
        result.Value.Birthday.Should().Be("1979-05-30");
        result.Value.HeightCm.Should().BeNull(); // AniList has no height
        result.Value.WeightKg.Should().BeNull(); // AniList has no weight
        result.Value.ImageUrl.Should().Be("https://img/rie.png");
        upserted.Should().NotBeNull();
        upserted!.CachedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task ExplicitNameParts_ArePreferred_OverSplitFallback()
    {
        SetupFetch(Staff(new AniListStaff
        {
            Id = 7,
            Name = new AniListName { Full = "Rie Kugimiya", GivenName = "Rie", FamilyName = "Kugimiya" },
        }));

        var result = await _sut.GetOrFetchAsync(7);

        result.Value!.FamilyName.Should().Be("Kugimiya");
        result.Value.GivenName.Should().Be("Rie");
    }

    [Fact]
    public async Task SingleTokenName_FallsBackToGivenName()
    {
        SetupFetch(Staff(new AniListStaff { Id = 7, Name = new AniListName { Full = "Madonna" } }));

        var result = await _sut.GetOrFetchAsync(7);

        result.Value!.GivenName.Should().Be("Madonna");
        result.Value.FamilyName.Should().BeNull();
    }

    [Fact]
    public async Task BirthdayMonthDayOnly_IsFormattedAsPartial()
    {
        SetupFetch(Staff(new AniListStaff
        {
            Id = 7,
            Name = new AniListName { Full = "Someone" },
            DateOfBirth = new AniListFuzzyDate { Year = null, Month = 5, Day = 30 },
        }));

        var result = await _sut.GetOrFetchAsync(7);

        result.Value!.Birthday.Should().Be("--05-30");
    }

    [Fact]
    public async Task BirthdayWithoutMonthAndDay_IsNull()
    {
        SetupFetch(Staff(new AniListStaff
        {
            Id = 7,
            Name = new AniListName { Full = "Someone" },
            DateOfBirth = new AniListFuzzyDate { Year = 1979, Month = null, Day = null },
        }));

        var result = await _sut.GetOrFetchAsync(7);

        result.Value!.Birthday.Should().BeNull();
    }

    [Fact]
    public async Task ProviderOutage_ServesStaleCache()
    {
        _repository.Setup(r => r.GetByPersonIdAsync(7))
                   .ReturnsAsync(new VoiceActor { PersonId = 7, Name = "Stale", CachedAt = DateTime.UtcNow.AddDays(-60) });
        SetupFetch(AniListApiResult<AniListStaffData>.Down("connection refused"));

        var result = await _sut.GetOrFetchAsync(7);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Name.Should().Be("Stale");
        _repository.Verify(r => r.UpsertAsync(It.IsAny<VoiceActor>()), Times.Never);
    }

    [Fact]
    public async Task ProviderOutage_NoCache_ReturnsNull_For404Contract()
    {
        SetupFetch(AniListApiResult<AniListStaffData>.Down("connection refused"));

        var result = await _sut.GetOrFetchAsync(7);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task UnknownStaffId_ReturnsNull()
    {
        SetupFetch(Staff(null)); // data.Staff == null — AniList's definitive "no such staff"

        var result = await _sut.GetOrFetchAsync(7);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task MissingStaffId_FallsBackToRequestedPersonId()
    {
        SetupFetch(Staff(new AniListStaff { Id = 0, Name = new AniListName { Full = "No Id" } }));

        var result = await _sut.GetOrFetchAsync(7);

        result.Value!.PersonId.Should().Be(7);
    }

    [Fact]
    public async Task RepositoryFailure_ReturnsDedicatedFailure_NotThrow()
    {
        _repository.Setup(r => r.GetByPersonIdAsync(It.IsAny<int>())).ThrowsAsync(new InvalidOperationException("db down"));

        var result = await _sut.GetOrFetchAsync(7);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(Constants.Errors.GetVoiceActorProfileAsync);
    }
}
