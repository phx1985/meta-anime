using FluentAssertions;
using MetaAnime.Application;
using MetaAnime.Application.Services;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace MetaAnime.UnitTests.Services;

public class CastIdSpaceMigratorTests
{
    private readonly Mock<IAnimeCastRepository> _castRepository = new();
    private readonly Mock<IVoiceActorRepository> _vaRepository = new();
    private readonly Mock<IPropertyBagRepository> _propertyBag = new();
    private readonly CastIdSpaceMigrator _sut;

    public CastIdSpaceMigratorTests()
    {
        _sut = new CastIdSpaceMigrator(
            _castRepository.Object, _vaRepository.Object, _propertyBag.Object, NullLogger<CastIdSpaceMigrator>.Instance);

        _castRepository.Setup(r => r.DeleteAllAsync()).Returns(Task.CompletedTask);
        _vaRepository.Setup(r => r.DeleteAllAsync()).Returns(Task.CompletedTask);
        _propertyBag.Setup(p => p.UpsertAsync(It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);
    }

    private void SetupMarker(string? value) =>
        _propertyBag.Setup(p => p.GetByKeyAsync(CastIdSpaceMigrator.MarkerKey))
                    .ReturnsAsync(value is null
                        ? null
                        : new PropertyBag
                        {
                            Key = CastIdSpaceMigrator.MarkerKey,
                            Value = value,
                            CreationDate = DateTime.UtcNow,
                            UpdateDate = DateTime.UtcNow,
                        });

    [Fact]
    public async Task MissingMarker_ClearsBothTables_AndSetsAniListMarker()
    {
        SetupMarker(null);

        var result = await _sut.EnsureMigratedAsync();

        result.IsSuccess.Should().BeTrue();
        _castRepository.Verify(r => r.DeleteAllAsync(), Times.Once);
        _vaRepository.Verify(r => r.DeleteAllAsync(), Times.Once);
        _propertyBag.Verify(p => p.UpsertAsync(CastIdSpaceMigrator.MarkerKey, CastIdSpaceMigrator.AniListIdSpace), Times.Once);
    }

    [Fact]
    public async Task ForeignMarker_IsTreatedAsUnmigrated_AndResetToAniList()
    {
        SetupMarker("jikan-mal-v1");

        var result = await _sut.EnsureMigratedAsync();

        result.IsSuccess.Should().BeTrue();
        _castRepository.Verify(r => r.DeleteAllAsync(), Times.Once);
        _vaRepository.Verify(r => r.DeleteAllAsync(), Times.Once);
        _propertyBag.Verify(p => p.UpsertAsync(CastIdSpaceMigrator.MarkerKey, CastIdSpaceMigrator.AniListIdSpace), Times.Once);
    }

    [Fact]
    public async Task CurrentMarker_IsIdempotentNoOp()
    {
        SetupMarker(CastIdSpaceMigrator.AniListIdSpace);

        (await _sut.EnsureMigratedAsync()).IsSuccess.Should().BeTrue();
        (await _sut.EnsureMigratedAsync()).IsSuccess.Should().BeTrue();

        _castRepository.Verify(r => r.DeleteAllAsync(), Times.Never);
        _vaRepository.Verify(r => r.DeleteAllAsync(), Times.Never);
        _propertyBag.Verify(p => p.UpsertAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RepositoryFailure_ReturnsDedicatedFailure_NotThrow()
    {
        SetupMarker(null);
        _castRepository.Setup(r => r.DeleteAllAsync()).ThrowsAsync(new InvalidOperationException("db down"));

        var result = await _sut.EnsureMigratedAsync();

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(Constants.Errors.MigrateCastIdSpace);
    }
}
