using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Interfaces.Shared.Results;
using MetaAnime.Core.Models;

namespace MetaAnime.Application.Services
{
    public class AnimeMappingService(IAnimeMappingRepository mappingRepository) : IAnimeMappingService
    {
        public async Task<Result> BulkDeleteAllAsync()
        {
            try
            {
                await mappingRepository.BulkDeleteAllAsync();
                return Result.Success();
            }
            catch (Exception ex)
            {
                return Result.Failure(new("Error.BulkDeleteAll", ex.Message));
            }
        }

        public async Task<Result> BulkInsertAsync(IEnumerable<AnimeMapping> mappings)
        {
            try
            {
                await mappingRepository.BulkInsertAsync(mappings);
                return Result.Success();
            }
            catch (Exception ex)
            {
                return Result.Failure(new("Error.BulkInsertAsync", ex.Message));
            }
        }

        public async Task<Result<IEnumerable<AnimeMapping>>> GetAllAsync(bool includeAnime = false)
        {
            try
            {
                var mappings = await mappingRepository.GetAllAsync(includeAnime);
                return Result.Success(mappings);
            }
            catch (Exception ex)
            {
                return Result.Failure<IEnumerable<AnimeMapping>>(new("Error.GetAllAsync", ex.Message));
            }
        }
    }
}
