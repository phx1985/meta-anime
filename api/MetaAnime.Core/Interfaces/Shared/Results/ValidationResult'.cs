using System.Diagnostics.CodeAnalysis;

namespace MetaAnime.Core.Interfaces.Shared.Results;

/// <summary>
///     Represents the result of a validation.
/// </summary>
/// <typeparam name="TValue"></typeparam>
[ExcludeFromCodeCoverage]
public sealed class ValidationResult<TValue> : Result<TValue>, IValidationResult
{
    private ValidationResult(IReadOnlyList<Error> errors)
        : base(default!, false, ValidationResult.ValidationError)
    {
        Errors = errors;
    }

    /// <inheritdoc />
    public IReadOnlyList<Error> Errors { get; }

    /// <summary>
    ///     Creates a new instance of <see cref="ValidationResult{TValue}" /> for a successful validation.
    /// </summary>
    /// <param name="errors"></param>
    /// <returns></returns>
    [SuppressMessage("Design", "CA1000: Do not declare static members on generic types")]
    public static ValidationResult<TValue> WithErrors(Error[] errors)
    {
        return new ValidationResult<TValue>(errors);
    }
}
