using System.Globalization;
using MetaAnime.Application.Services.AniList;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Interfaces.Shared.Results;
using MetaAnime.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace MetaAnime.Application.Services;

/// <summary>
/// Voice actor profiles from the AniList GraphQL API (Staff query, by AniList person id),
/// cached in the <see cref="VoiceActor"/> table with a 30-day TTL: fresh rows are served as-is,
/// stale or missing rows trigger one lazy fetch through the pacing queue followed by an upsert.
/// A stale row is preferred over a hard failure; null means AniList has no profile (→ 404).
/// AniList carries no height/weight — those columns are stored as NULL.
/// </summary>
public sealed class AniListPersonService(
    IVoiceActorRepository voiceActorRepository,
    IAniListClient anilistClient,
    CastIdSpaceMigrator idSpaceMigrator,
    ILogger<AniListPersonService> logger)
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromDays(30);

    /// <summary>
    /// Returns the cached VA profile if younger than 30 days, otherwise fetches the AniList
    /// Staff profile and persists it. Returns Success(null) when AniList has no profile and
    /// nothing is cached.
    /// </summary>
    public async Task<Result<VoiceActor?>> GetOrFetchAsync(int personId)
    {
        try
        {
            // Stale MAL-ID rows would masquerade as fresh cache hits — clear the old ID space first.
            var migrated = await idSpaceMigrator.EnsureMigratedAsync();
            if (migrated.IsFailure)
                return Result.Failure<VoiceActor?>(migrated.Error);

            var cached = await voiceActorRepository.GetByPersonIdAsync(personId);
            if (cached is not null && DateTime.UtcNow - cached.CachedAt < CacheTtl)
                return Result.Success<VoiceActor?>(cached);

            logger.LogInformation(Constants.LogMessages.AniListPersonService.Fetching, personId);
            var fetched = await anilistClient.PostQueryAsync<AniListStaffData>(
                AniListQueries.StaffProfile,
                new Dictionary<string, object?> { ["id"] = personId });

            if (fetched.Status != AniListApiStatus.Success || fetched.Data?.Staff is null)
            {
                // Unreachable or definitive miss: stale cache beats a hard failure.
                logger.LogWarning(Constants.LogMessages.AniListPersonService.FetchFailed, personId);
                return Result.Success<VoiceActor?>(cached);
            }

            var va = MapToVoiceActor(personId, fetched.Data.Staff);
            await voiceActorRepository.UpsertAsync(va);
            return Result.Success<VoiceActor?>(va);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, Constants.LogMessages.AniListPersonService.FetchFailed, personId);
            return Result.Failure<VoiceActor?>(new(Constants.Errors.GetVoiceActorProfileAsync, ex.Message));
        }
    }

    private static VoiceActor MapToVoiceActor(int personId, AniListStaff staff)
    {
        var (familyName, givenName) = ResolveNameParts(staff.Name);
        return new VoiceActor
        {
            PersonId   = staff.Id > 0 ? staff.Id : personId,
            Name       = staff.Name?.Full ?? string.Empty,
            FamilyName = familyName,
            GivenName  = givenName,
            ImageUrl   = staff.Image?.GetImageUrl(),
            Birthday   = FormatBirthday(staff.DateOfBirth),
            BloodType  = staff.BloodType,
            // AniList Staff has no height/weight — the contract keeps them nullable.
            HeightCm   = null,
            WeightKg   = null,
            CachedAt   = DateTime.UtcNow,
        };
    }

    /// <summary>
    /// given/family from the upstream's explicit name parts when present, else split the full
    /// name: AniList renders full names in given-then-family order, so the last token is the
    /// family name and everything before it the given name.
    /// </summary>
    private static (string? FamilyName, string? GivenName) ResolveNameParts(AniListName? name)
    {
        if (!string.IsNullOrWhiteSpace(name?.FamilyName) || !string.IsNullOrWhiteSpace(name?.GivenName))
            return (NullIfBlank(name?.FamilyName), NullIfBlank(name?.GivenName));

        var full = name?.Full?.Trim();
        if (string.IsNullOrWhiteSpace(full))
            return (null, null);

        var lastSpace = full.LastIndexOf(' ');
        if (lastSpace <= 0)
            return (null, full); // single-token name: treat as given name

        return (full[(lastSpace + 1)..], full[..lastSpace]);
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Formats AniList's fuzzy date: full when year+month+day are present (YYYY-MM-DD), the
    /// partial "--MM-DD" when only month+day are known, else null.
    /// </summary>
    private static string? FormatBirthday(AniListFuzzyDate? dob)
    {
        if (dob?.Month is { } month && dob.Day is { } day)
        {
            if (dob.Year is { } year)
                return string.Create(CultureInfo.InvariantCulture, $"{year:0000}-{month:00}-{day:00}");
            return string.Create(CultureInfo.InvariantCulture, $"--{month:00}-{day:00}");
        }

        return null;
    }
}
