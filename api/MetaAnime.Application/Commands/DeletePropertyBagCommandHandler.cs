using MediatR;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Application.Commands;

public sealed class DeletePropertyBagCommandHandler(IPropertyBagRepository propertyBagRepository)
    : IRequestHandler<DeletePropertyBagCommand, Result>
{
    public async Task<Result> Handle(DeletePropertyBagCommand request, CancellationToken cancellationToken)
    {
        try
        {
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
