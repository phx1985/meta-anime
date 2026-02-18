namespace MetaAnime.Core.Interfaces.Shared.Results;

/// <summary>
///     Validation result
/// </summary>
public interface IValidationResult
{
    /// <summary>
    ///     Errors
    /// </summary>
    IReadOnlyList<Error> Errors { get; }
}
