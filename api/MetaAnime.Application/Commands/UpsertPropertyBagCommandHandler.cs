using Mediator;
using MetaAnime.Application.Contracts;
using MetaAnime.Application.Interfaces;
using MetaAnime.Core;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Commands;

public sealed class UpsertPropertyBagCommandHandler(
    IPropertyBagRepository propertyBagRepository,
    ISecretProtector secretProtector)
    : IRequestHandler<UpsertPropertyBagCommand, Result<PropertyBagResponse>>
{
    public async ValueTask<Result<PropertyBagResponse>> Handle(UpsertPropertyBagCommand request, CancellationToken cancellationToken)
    {
        try
        {
            // Encrypt sensitive values at rest (e.g. the Bot-kun provider API key).
            // The response echoes the plaintext so API behaviour is unchanged.
            var storedValue = SensitiveKeys.Contains(request.Key) && !string.IsNullOrEmpty(request.Request.Value)
                ? secretProtector.Protect(request.Request.Value)
                : request.Request.Value;

            await propertyBagRepository.UpsertAsync(request.Key, storedValue);
            var entry = await propertyBagRepository.GetByKeyAsync(request.Key);
            return Result.Success(new PropertyBagResponse(entry!.Key, request.Request.Value, entry.CreationDate, entry.UpdateDate));
        }
        catch (Exception ex)
        {
            return Result.Failure<PropertyBagResponse>(new(Constants.Errors.UpsertPropertyBag, ex.Message));
        }
    }

    private static readonly HashSet<string> SensitiveKeys =
    [
        SettingsKeys.BotKunApiKey,
    ];
}
