using System.Diagnostics.CodeAnalysis;

namespace MetaAnime.Core.Interfaces.Shared.Results;

/// <summary>
///     Extension methods for <see cref="Result{T}" />.
/// </summary>
public static class ResultExtensions
{
    /// <summary>
    ///    Ensures that the result validate predicate.
    /// </summary>
    /// <param name="result"></param>
    /// <param name="predicate"></param>
    /// <param name="error"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public static Result<T> Ensure<T>(this Result<T> result, Func<T, bool> predicate, Error error)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(predicate);

        if (result.IsFailure)
        {
            return result;
        }

        return predicate(result.Value) ? result : Result.Failure<T>(error);
    }

    /// <summary>
    ///     Maps <see cref="Result{T}" /> to <see cref="Result{TOut}" />.
    /// </summary>
    /// <param name="result"></param>
    /// <param name="mapping"></param>
    /// <typeparam name="TIn"></typeparam>
    /// <typeparam name="TOut"></typeparam>
    /// <returns></returns>
    public static Result<TOut> Map<TIn, TOut>(this Result<TIn> result, Func<TIn, TOut> mapping)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(mapping);

        return result.IsSuccess ? Result.Success(mapping(result.Value)) : Result.Failure<TOut>(result.Error);
    }

    /// <summary>
    ///     Maps <see cref="Result{T}" /> to <see cref="Result{TOut}" />.
    /// </summary>
    /// <param name="result"></param>
    /// <param name="mapping"></param>
    /// <typeparam name="TIn"></typeparam>
    /// <typeparam name="TOut"></typeparam>
    /// <returns></returns>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types")]
    public static Task<Result<TOut>> Map<TIn, TOut>(this Task<Result<TIn>> result, Func<TIn, Task<TOut>> mapping)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(mapping);

        var tcs = new TaskCompletionSource<Result<TOut>>();

        _ = result.ContinueWith(task =>
        {
            if (task.Status == TaskStatus.RanToCompletion)
            {
                if (task.Result.IsSuccess)
                {
                    var input = task.Result.Value;

                    try
                    {
                        var mappedTask = mapping(input);
                        _ = mappedTask.ContinueWith(mappedTaskResult =>
                        {
                            if (mappedTaskResult.Status == TaskStatus.RanToCompletion)
                            {
                                var mappedResult = mappedTaskResult.Result;
                                tcs.SetResult(Result.Success(mappedResult));
                            }
                            else if (mappedTaskResult.Status == TaskStatus.Faulted)
                            {
                                // Handle any exceptions that occurred during mapping
                                tcs.SetResult(Result.Failure<TOut>(
                                    new Error(
                                        "MAPPING_ERROR",
                                        mappedTaskResult.Exception?.InnerException?.Message ?? "Unknown error")));
                            }
                            else if (mappedTaskResult.Status == TaskStatus.Canceled)
                            {
                                tcs.SetCanceled();
                            }
                        }, TaskScheduler.Current);
                    }
                    catch (Exception ex)
                    {
                        // Handle any exceptions that occurred during mapping
                        tcs.SetResult(Result.Failure<TOut>(
                            new Error(
                                "MAPPING_ERROR",
                                ex.InnerException?.Message ?? "Unknown error")));
                    }
                }
                else
                {
                    // Propagate the failure result without mapping
                    tcs.SetResult(Result.Failure<TOut>(task.Result.Error));
                }
            }
            else if (task.Status == TaskStatus.Faulted)
            {
                // Propagate the error from the input task
                tcs.SetResult(Result.Failure<TOut>(
                    new Error(
                        "UNKNOWN",
                        task.Exception?.InnerException?.Message ?? "Unknown error")));
            }
            else if (task.Status == TaskStatus.Canceled)
            {
                tcs.SetCanceled();
            }
        }, TaskScheduler.Current);

        return tcs.Task;
    }

    // /// <summary>
    // ///     Bind <see cref="Result{T}" /> to <see cref="Result" />.
    // /// </summary>
    // /// <param name="result"></param>
    // /// <param name="func"></param>
    // /// <typeparam name="TIn"></typeparam>
    // /// <returns></returns>
    // public static async Task<Result> Bind<TIn>(this Result<TIn> result, Func<TIn, Task<Result>> func)
    // {
    //     ArgumentNullException.ThrowIfNull(result);
    //     ArgumentNullException.ThrowIfNull(func);
    //
    //     return result.IsSuccess ? await func(result.Value) : Result.Failure(result.Error);
    // }
    //
    // /// <summary>
    // ///     Bind <see cref="Result{T}" /> to <see cref="Result" />.
    // /// </summary>
    // /// <param name="result"></param>
    // /// <param name="func"></param>
    // /// <typeparam name="TIn"></typeparam>
    // /// <typeparam name="TOut"></typeparam>
    // /// <returns></returns>
    // public static async Task<Result<TOut>> Bind<TIn, TOut>(this Result<TIn> result, Func<TIn, Task<Result<TOut>>> func)
    // {
    //     ArgumentNullException.ThrowIfNull(result);
    //     ArgumentNullException.ThrowIfNull(func);
    //
    //     return result.IsSuccess ? await func(result.Value) : Result.Failure<TOut>(result.Error);
    // }

    /// <summary>
    ///     Binds <see cref="Result{T}" /> to <see cref="Result{TOut}" />.
    /// </summary>
    /// <param name="result"></param>
    /// <param name="action"></param>
    /// <typeparam name="TIn"></typeparam>
    /// <typeparam name="TOut"></typeparam>
    /// <returns></returns>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types")]
    public static Task<Result<TOut>> Bind<TIn, TOut>(this Task<Result<TIn>> result, Func<TIn, TOut> action)
    {
        ArgumentNullException.ThrowIfNull(result);

        var tcs = new TaskCompletionSource<Result<TOut>>();

        _ = result.ContinueWith(task =>
        {
            if (task.IsFaulted)
            {
                tcs.SetException(task.Exception);
            }
            else if (task.IsCanceled)
            {
                tcs.SetCanceled();
            }
            else
            {
                try
                {
                    var inputValue = task.Result.Value;
                    var outputValue = action(inputValue);
                    tcs.SetResult(Result.Success(outputValue));
                }
                catch (Exception ex)
                {
                    tcs.SetResult(Result.Failure<TOut>(new Error("UNKNOWN", ex.Message)));
                }
            }
        }, TaskScheduler.Current);

        return tcs.Task;
    }

    /// <summary>
    ///     Taps with the result of a <see cref="Result{T}" />.
    /// </summary>
    /// <param name="result"></param>
    /// <param name="action"></param>
    /// <typeparam name="TIn"></typeparam>
    /// <returns></returns>
    public static Task<Result<TIn>> Tap<TIn>(this Task<Result<TIn>> result, Func<TIn, Task> action)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(action);

        var tcs = new TaskCompletionSource<Result<TIn>>();

        _ = result.ContinueWith(task =>
        {
            if (task.IsFaulted)
            {
                tcs.SetResult(Result.Failure<TIn>(new Error("UNKNOWN", task.Exception?.Message ?? "Unknown error")));
                return;
            }

            if (task.IsCanceled)
            {
                tcs.SetCanceled();
                return;
            }

            // Extract the result from the completed task
            var input = task.Result;

            if (input.IsSuccess)
            {
                // Perform the mapping action asynchronously
                var mappingTask = action(input.Value);

                _ = mappingTask.ContinueWith(mappingResultTask =>
                {
                    if (mappingResultTask.IsFaulted)
                    {
                        tcs.SetResult(Result.Failure<TIn>(new Error("UNKNOWN",
                            mappingResultTask.Exception?.Message ?? "Unknown error")));
                        return;
                    }

                    if (mappingResultTask.IsCanceled)
                    {
                        tcs.SetCanceled();
                        return;
                    }

                    // Set the result with the original input
                    tcs.SetResult(input);
                }, TaskScheduler.Current);
            }
            else
            {
                tcs.SetResult(Result.Failure<TIn>(input.Error));
            }
        }, TaskScheduler.Current);

        return tcs.Task;
    }
}
