namespace MetaAnime.Core.Models;

/// <summary>
/// Outcome of a conditional fetch against a remote document.
/// When <see cref="NotModified"/> is true the remote answered 304 and
/// <see cref="Items"/> is null: the caller must not touch its local copy.
/// </summary>
public sealed record KometaFetchOutcome<T>(
    bool NotModified,
    IList<T>? Items,
    string? ETag,
    string? LastModified)
    where T : class;
