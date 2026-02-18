namespace MetaAnime.Application.DTOs
{
    public sealed record PaginatedResult<T>(
        IEnumerable<T> Items,
        int Page,
        int PageSize,
        int TotalCount,
        int TotalPages
    );
}
