using Mediator;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Commands;

public sealed class DeletePropertyBagCommandHandler(IPropertyBagRepository propertyBagRepository)
    : IRequestHandler<DeletePropertyBagCommand, Result>
{
    public async ValueTask<Result> Handle(DeletePropertyBagCommand request, CancellationToken cancellationToken)
    {
        try
        {
            // Internal System:* keys are never touched through the generic
            // property-bag API — report them exactly like missing keys.
            if (PropertyBagKeyPolicy.IsSystemManaged(request.Key))
            {
                return Result.Failure(new Error(Constants.Errors.NotFound, $"PropertyBag entry with key '{request.Key}' was not found"));
            }

            var existing = await propertyBagRepository.GetByKeyAsync(request.Key);
            if (existing is null)
            {
                return Result.Failure(new Error(Constants.Errors.NotFound, $"PropertyBag entry with key '{request.Key}' was not found"));
            }

            await propertyBagRepository.DeleteAsync(request.Key);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(new(Constants.Errors.DeletePropertyBag, ex.Message));
        }
    }
}
