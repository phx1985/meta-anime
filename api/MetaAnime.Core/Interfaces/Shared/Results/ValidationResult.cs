using System.Diagnostics.CodeAnalysis;

namespace MetaAnime.Core.Interfaces.Shared.Results;

/// <summary>
///     Represents the result of a validation operation.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class ValidationResult : Result, IValidationResult
{
    internal static readonly Error ValidationError = new(
        "ValidationError",
        "A validation problem occurred.");

    private ValidationResult(IReadOnlyList<Error> errors)
        : base(false, ValidationError)
    {
        Errors = errors;
    }

    /// <inheritdoc />
    public IReadOnlyList<Error> Errors { get; }

    /// <summary>
    ///     Creates a new validation result with the specified errors.
    /// </summary>
    /// <param name="errors"></param>
    /// <returns></returns>
    public static ValidationResult WithErrors(Error[] errors)
    {
        return new ValidationResult(errors);
    }
}
