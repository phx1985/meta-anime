using System.Diagnostics.CodeAnalysis;

namespace MetaAnime.Core.Interfaces.Shared.Results;

/// <summary>
///     Represents the result of an operation.
/// </summary>
/// <typeparam name="TValue"></typeparam>
[ExcludeFromCodeCoverage]
public class Result<TValue> : Result
{
    private readonly TValue _value;

    /// <summary>
    ///     Creates a new <see cref="Result{TValue}" /> instance.
    /// </summary>
    /// <param name="value"></param>
    /// <param name="isSuccess"></param>
    /// <param name="error"></param>
    protected internal Result(TValue value, bool isSuccess, Error error)
        : base(isSuccess, error)
    {
        _value = value;
    }

    /// <summary>
    ///     Creates a new successful <see cref="Result{TValue}" /> instance.
    /// </summary>
    /// <exception cref="InvalidOperationException"></exception>
    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("The value of a failure result can not be accessed.");

    /// <summary>
    ///     Creates a new successful <see cref="Result{TValue}" /> instance.
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    [SuppressMessage("Design", "CA2225: CA2225: Operator overloads have named alternates")]
    public static implicit operator Result<TValue>(TValue value)
    {
        return Create(value);
    }
}
