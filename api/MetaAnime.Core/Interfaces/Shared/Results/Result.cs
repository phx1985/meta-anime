using System.Diagnostics.CodeAnalysis;

namespace MetaAnime.Core.Interfaces.Shared.Results;

/// <summary>
///     Represents the result of an operation.
/// </summary>
[ExcludeFromCodeCoverage]
public class Result
{
    /// <summary>
    ///     Creates a new <see cref="Result" /> instance.
    /// </summary>
    /// <param name="isSuccess"></param>
    /// <param name="error"></param>
    /// <exception cref="InvalidOperationException"></exception>
    protected Result(bool isSuccess, Error error)
    {
        switch (isSuccess)
        {
            case true when error != Error.None:
                throw new InvalidOperationException();
            case false when error == Error.None:
                throw new InvalidOperationException();
            default:
                IsSuccess = isSuccess;
                Error = error;
                break;
        }
    }

    /// <summary>
    ///     Indicates whether the operation was successful.
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    ///     Indicates whether the operation was a failure.
    /// </summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>
    ///     The error that occurred during the operation.
    /// </summary>
    public Error Error { get; }

    /// <summary>
    ///     Creates a new successful <see cref="Result" /> instance.
    /// </summary>
    /// <returns></returns>
    public static Result Success()
    {
        return new Result(true, Error.None);
    }

    /// <summary>
    ///     Creates a new successful <see cref="Result" /> instance.
    /// </summary>
    /// <param name="value"></param>
    /// <typeparam name="TValue"></typeparam>
    /// <returns></returns>
    public static Result<TValue> Success<TValue>(TValue value)
    {
        return new Result<TValue>(value, true, Error.None);
    }

    /// <summary>
    ///     Creates a new failure <see cref="Result" /> instance.
    /// </summary>
    /// <param name="error"></param>
    /// <returns></returns>
    public static Result Failure(Error error)
    {
        return new Result(false, error);
    }

    /// <summary>
    ///     Creates a new failure <see cref="Result" /> instance.
    /// </summary>
    /// <param name="error"></param>
    /// <typeparam name="TValue"></typeparam>
    /// <returns></returns>
    public static Result<TValue> Failure<TValue>(Error error)
    {
        return new Result<TValue>(default!, false, error);
    }

    /// <summary>
    ///     Creates a new failure <see cref="Result" /> instance.
    /// </summary>
    /// <param name="value"></param>
    /// <typeparam name="TValue"></typeparam>
    /// <returns></returns>
    public static Result<TValue> Create<TValue>(TValue? value)
    {
        return value is not null ? Success(value) : Failure<TValue>(Error.NullValue);
    }


    /// <summary>
    ///     Creates a new failure <see cref="Result" /> instance.
    /// </summary>
    /// <param name="value"></param>
    /// <typeparam name="TValue"></typeparam>
    /// <returns></returns>
    public static async Task<Result<TValue>> Create<TValue>(Task<TValue> value)
    {
        var result = await value;
        return result is not null ? Success(result) : Failure<TValue>(Error.NullValue);
    }

    /// <summary>
    ///     Creates a new failure <see cref="Result" /> instance.
    /// </summary>
    /// <param name="value"></param>
    /// <typeparam name="TValue"></typeparam>
    /// <returns></returns>
    public static Result<TValue?> CreateNullable<TValue>(TValue? value)
    {
        return Success(value);
    }
}
