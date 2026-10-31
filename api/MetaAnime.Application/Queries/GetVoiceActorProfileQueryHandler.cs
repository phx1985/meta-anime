using Mediator;
using MetaAnime.Application.Contracts;
using MetaAnime.Application.Services;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Queries;

public sealed class GetVoiceActorProfileQueryHandler(AniListPersonService anilistPersonService)
    : IRequestHandler<GetVoiceActorProfileQuery, Result<VoiceActorProfileResponse?>>
{
    public async ValueTask<Result<VoiceActorProfileResponse?>> Handle(GetVoiceActorProfileQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var vaResult = await anilistPersonService.GetOrFetchAsync(request.PersonId);
            if (vaResult.IsFailure)
                return Result.Failure<VoiceActorProfileResponse?>(vaResult.Error);

            var va = vaResult.Value;
            if (va is null)
                return Result.Success<VoiceActorProfileResponse?>(null);

            int? age = null;
            if (!string.IsNullOrWhiteSpace(va.Birthday)
                && DateOnly.TryParse(va.Birthday, out var bd))
            {
                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                age = today.Year - bd.Year;
                if (today < bd.AddYears(age.Value)) age--;
            }

            return Result.Success<VoiceActorProfileResponse?>(new VoiceActorProfileResponse(
                va.PersonId,
                va.Name,
                va.FamilyName,
                va.GivenName,
                va.ImageUrl,
                age,
                va.BloodType,
                va.HeightCm,
                va.WeightKg,
                va.Birthday));
        }
        catch (Exception ex)
        {
            return Result.Failure<VoiceActorProfileResponse?>(new(Constants.Errors.GetVoiceActorProfileAsync, ex.Message));
        }
    }
}
