using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Interfaces.Shared.Results;
using MetaAnime.Domain.Entities;
using MetaAnime.Domain.Enums;

namespace MetaAnime.Application.Services;

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

    public async Task<Result<(List<string> AniDbIds, int TotalCount)>> GetPagedAniDbIdsAsync(
        int page, int pageSize,
        string? titleFilter = null,
        AnimeSource? sourceFilter = null,
        string? sortBy = null,
        bool sortDescending = false)
    {
        try
        {
            var result = await mappingRepository.GetPagedAniDbIdsAsync(
                page, pageSize, titleFilter, sourceFilter, sortBy, sortDescending);
            return Result.Success(result);
        }
        catch (Exception ex)
        {
            return Result.Failure<(List<string>, int)>(new("Error.GetPagedAniDbIdsAsync", ex.Message));
        }
    }

    public async Task<Result<List<string>>> GetAllFilteredAniDbIdsAsync(
        string? titleFilter = null,
        AnimeSource? sourceFilter = null)
    {
        try
        {
            var result = await mappingRepository.GetAllFilteredAniDbIdsAsync(titleFilter, sourceFilter);
            return Result.Success(result);
        }
        catch (Exception ex)
        {
            return Result.Failure<List<string>>(new("Error.GetAllFilteredAniDbIdsAsync", ex.Message));
        }
    }

    public async Task<Result<IEnumerable<AnimeMapping>>> GetByAniDbIdsAsync(IEnumerable<string> aniDbIds)
    {
        try
        {
            var mappings = await mappingRepository.GetByAniDbIdsAsync(aniDbIds);
            return Result.Success(mappings);
        }
        catch (Exception ex)
        {
            return Result.Failure<IEnumerable<AnimeMapping>>(new("Error.GetByAniDbIdsAsync", ex.Message));
        }
    }
}
