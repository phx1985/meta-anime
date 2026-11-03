using MetaAnime.Core.Interfaces.Shared.Results;
using MetaAnime.Core.Models;

namespace MetaAnime.Core.Interfaces.Services;

public interface IKometaAnimeJsonService<T>
    where T : class
{
    Task<Result<IList<T>>> GetAllAsync();

    /// <summary>
    /// Conditional fetch: sends <c>If-None-Match</c>/<c>If-Modified-Since</c> when the caller
    /// supplies stored validators (unless <paramref name="forceRefresh"/> is set) and reports
    /// 304 responses as <see cref="KometaFetchOutcome{T}.NotModified"/> so the caller can skip
    /// all local writes when the remote document is unchanged.
    /// </summary>
    Task<Result<KometaFetchOutcome<T>>> GetIfModifiedAsync(string? etag, string? lastModified, bool forceRefresh = false);
}
