using Mediator;
using MetaAnime.Application.Contracts;
using MetaAnime.Application.Interfaces;
using MetaAnime.Application.Services;
using MetaAnime.Core;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Commands;

/// <summary>
/// First-run setup wizard password creation. Succeeds ONLY while no admin
/// password exists yet — once one is configured this command always rejects,
/// so the anonymous wizard endpoint can never overwrite an existing password.
/// </summary>
public sealed class SetupPasswordCommandHandler(
    IPropertyBagRepository propertyBagRepository,
    IAdminAuthService adminAuthService)
    : IRequestHandler<SetupPasswordCommand, Result<LoginResponse>>
{
    public async ValueTask<Result<LoginResponse>> Handle(SetupPasswordCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var hashEntry = await propertyBagRepository.GetByKeyAsync(SettingsKeys.AdminPasswordHash);
            if (!string.IsNullOrWhiteSpace(hashEntry?.Value))
                return Result.Failure<LoginResponse>(new(Constants.Errors.AuthPasswordAlreadySet, "Admin password is already configured."));

            if (string.IsNullOrWhiteSpace(request.Password))
                return Result.Failure<LoginResponse>(new(Constants.Errors.AuthEmptyPassword, "Password cannot be empty."));

            var hash = adminAuthService.HashPassword(request.Password);
            await propertyBagRepository.UpsertAsync(SettingsKeys.AdminPasswordHash, hash);

            return Result.Success(await AdminAuthSession.IssueLoginAsync(propertyBagRepository, adminAuthService));
        }
        catch (Exception ex)
        {
            return Result.Failure<LoginResponse>(new(Constants.Errors.SetupPassword, ex.Message));
        }
    }
}
