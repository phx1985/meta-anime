using MetaAnime.Domain.Enums;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Interfaces.Shared.Results;
using MetaAnime.Domain.Entities;

namespace MetaAnime.Application.Services;

public sealed class KometaAnimeMappingService(IKometaAnimeMappingRepository kometaAnimeMappingRepository) : IKometaAnimeMappingService
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
            return Result.Failure<IList<KometaAnimeMapping>>(new Error(Constants.Errors.GetAllAsync, ex.Message));
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
            return Result.Failure<IList<KometaAnimeMapping>>(new Error(Constants.Errors.GetByAniDbIdAsync, ex.Message));
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
            return Result.Failure<KometaAnimeMapping?>(new Error(Constants.Errors.GetByPlatformIdAsync, ex.Message));
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
            return Result.Failure(new Error(Constants.Errors.BulkDeleteAllAsync, ex.Message));
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
            return Result.Failure(new Error(Constants.Errors.BulkInsertAsync, ex.Message));
        }
    }

    public async Task<Result> MergeAsync(IEnumerable<KometaAnimeMapping> kometaAnimeMappings)
    {
        try
        {
            await kometaAnimeMappingRepository.MergeAsync(kometaAnimeMappings);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(new Error(Constants.Errors.MergeAsync, ex.Message));
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
            return Result.Failure<bool>(new Error(Constants.Errors.AnyAsync, ex.Message));
        }
    }
}
