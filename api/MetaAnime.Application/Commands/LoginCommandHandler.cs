using Mediator;
using MetaAnime.Application.Contracts;
using MetaAnime.Application.Interfaces;
using MetaAnime.Application.Services;
using MetaAnime.Core;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Commands;

public sealed class LoginCommandHandler(
    IPropertyBagRepository propertyBagRepository,
    IAdminAuthService adminAuthService)
    : IRequestHandler<LoginCommand, Result<LoginResponse>>
{
    public async ValueTask<Result<LoginResponse>> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var hashEntry = await propertyBagRepository.GetByKeyAsync(SettingsKeys.AdminPasswordHash);
            if (string.IsNullOrWhiteSpace(hashEntry?.Value))
                return Result.Failure<LoginResponse>(new(Constants.Errors.AuthNotConfigured, "Authentication is not configured."));

            if (!adminAuthService.VerifyPassword(request.Password, hashEntry.Value))
                return Result.Failure<LoginResponse>(new(Constants.Errors.AuthInvalidPassword, "Invalid password."));

            return Result.Success(await AdminAuthSession.IssueLoginAsync(propertyBagRepository, adminAuthService));
        }
        catch (Exception ex)
        {
            return Result.Failure<LoginResponse>(new(Constants.Errors.Login, ex.Message));
        }
    }
}
