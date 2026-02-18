using MetaAnime.Domain.Enums;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Interfaces.Shared.Results;
using MetaAnime.Domain.Entities;

namespace MetaAnime.Application.Services
{
    public class KometaAnimeMappingService(IKometaAnimeMappingRepository kometaAnimeMappingRepository) : IKometaAnimeMappingService
    {
        public async Task<Result<IList<KometaAnimeMapping>>> GetAllAsync()
        {
            try
            {
                var result = await kometaAnimeMappingRepository.GetAllAsync();
                return Result.Success(result);
            }
            catch (Exception ex)
            {
                return Result.Failure<IList<KometaAnimeMapping>>(new Error("Error.GetAllAsync", ex.Message));
            }
        }

        public async Task<Result<IList<KometaAnimeMapping>>> GetByAniDbIdAsync(string aniDbId)
        {
            try
            {
                var result = await kometaAnimeMappingRepository.GetByAniDbIdAsync(aniDbId);
                return Result.Success(result);
            }
            catch (Exception ex)
            {
                return Result.Failure<IList<KometaAnimeMapping>>(new Error("Error.GetByAniDbIdAsync", ex.Message));
            }
        }

        public async Task<Result<KometaAnimeMapping?>> GetByPlatformIdAsync(KometaAnimePlatform animePlatform, string platformId)
        {
            try
            {
                var result = await kometaAnimeMappingRepository.GetByPlatformIdAsync(animePlatform, platformId);
                return Result.Success(result);
            }
            catch (Exception ex)
            {
                return Result.Failure<KometaAnimeMapping?>(new Error("Error.GetByPlatformIdAsync", ex.Message));
            }
        }

        public async Task<Result> BulkDeleteAllAsync()
        {
            try
            {
                await kometaAnimeMappingRepository.BulkDeleteAllAsync();
                return Result.Success();
            }
            catch (Exception ex)
            {
                return Result.Failure(new Error("Error.BulkDeleteAllAsync", ex.Message));
            }
        }

        public async Task<Result> BulkInsertAsync(IEnumerable<KometaAnimeMapping> kometaAnimeMappings)
        {
            try
            {
                await kometaAnimeMappingRepository.BulkInsertAsync(kometaAnimeMappings);
                return Result.Success();
            }
            catch (Exception ex)
            {
                return Result.Failure(new Error("Error.BulkInsertAsync", ex.Message));
            }
        }

        public async Task<Result<bool>> AnyAsync()
        {
            try
            {
                var result = await kometaAnimeMappingRepository.AnyAsync();
                return Result.Success(result);
            }
            catch (Exception ex)
            {
                return Result.Failure<bool>(new Error("Error.AnyAsync", ex.Message));
            }
        }
    }
}
