using Mediator;
using MetaAnime.Application.Interfaces;
using MetaAnime.Core;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Commands;

public sealed class ChangePasswordCommandHandler(
    IPropertyBagRepository propertyBagRepository,
    IAdminAuthService adminAuthService)
    : IRequestHandler<ChangePasswordCommand, Result>
{
    public async ValueTask<Result> Handle(ChangePasswordCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var hashEntry = await propertyBagRepository.GetByKeyAsync(SettingsKeys.AdminPasswordHash);
            var existingHash = hashEntry?.Value;

            // If a password already exists, verify it first.
            if (!string.IsNullOrWhiteSpace(existingHash))
            {
                if (!adminAuthService.VerifyPassword(request.CurrentPassword, existingHash))
                    return Result.Failure(new(Constants.Errors.AuthInvalidPassword, "Current password is incorrect."));
            }

            if (string.IsNullOrWhiteSpace(request.NewPassword))
                return Result.Failure(new(Constants.Errors.AuthEmptyPassword, "New password cannot be empty."));

            var newHash = adminAuthService.HashPassword(request.NewPassword);
            await propertyBagRepository.UpsertAsync(SettingsKeys.AdminPasswordHash, newHash);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(new(Constants.Errors.ChangePassword, ex.Message));
        }
    }
}
