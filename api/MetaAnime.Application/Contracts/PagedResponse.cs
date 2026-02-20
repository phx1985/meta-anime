namespace MetaAnime.Application.Contracts;

public sealed record PagedResponse<T>(
    T Data,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages,
    bool HasNextPage,
    bool HasPreviousPage
)
{
    public static PagedResponse<T> Create(T data, int page, int pageSize, int totalCount)
    {
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
        var hasNextPage = page < totalPages;
        var hasPreviousPage = page > 1;

        return new PagedResponse<T>(
            data,
            page,
            pageSize,
            totalCount,
            totalPages,
            hasNextPage,
            hasPreviousPage
        );
    }
}
