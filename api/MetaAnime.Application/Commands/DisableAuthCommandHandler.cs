using Mediator;
using MetaAnime.Application.Interfaces;
using MetaAnime.Core;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Commands;

public sealed class DisableAuthCommandHandler(
    IPropertyBagRepository propertyBagRepository,
    IAdminAuthService adminAuthService)
    : IRequestHandler<DisableAuthCommand, Result>
{
    public async ValueTask<Result> Handle(DisableAuthCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var hashEntry = await propertyBagRepository.GetByKeyAsync(SettingsKeys.AdminPasswordHash);
            if (string.IsNullOrWhiteSpace(hashEntry?.Value))
                return Result.Failure(new(Constants.Errors.AuthNotEnabled, "Authentication is not currently enabled."));

            if (!adminAuthService.VerifyPassword(request.CurrentPassword, hashEntry.Value))
                return Result.Failure(new(Constants.Errors.AuthInvalidPassword, "Current password is incorrect."));

            await propertyBagRepository.DeleteAsync(SettingsKeys.AdminPasswordHash);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(new(Constants.Errors.DisableAuth, ex.Message));
        }
    }
}
