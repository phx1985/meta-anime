using System.Diagnostics.CodeAnalysis;

namespace MetaAnime.Core.Interfaces.Shared.Results;

/// <summary>
///     Error
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class Error : IEquatable<Error>
{
    /// <summary>
    ///     None
    /// </summary>
    public static readonly Error None = new(string.Empty, string.Empty);

    /// <summary>
    ///     Unknown
    /// </summary>
    public static readonly Error NullValue = new("Error.NullValue", "The specified result value is null.");

    /// <summary>
    ///    Constructor
    /// </summary>
    /// <param name="message"></param>
    public Error(string message)
        : this("UNKNOWN", message)
    {
    }

    /// <summary>
    ///     Constructor
    /// </summary>
    /// <param name="code"></param>
    /// <param name="message"></param>
    public Error(string code, string message)
    {
        Code = code;
        Message = message;
    }

    /// <summary>
    /// Code of the error
    /// </summary>
    public string Code { get; }

    /// <summary>
    /// Message of the error
    /// </summary>
    public string Message { get; }

    /// <inheritdoc />
    public bool Equals(Error? other)
    {
        if (other is null)
        {
            return false;
        }

        return Code == other.Code && Message == other.Message;
    }

    /// <summary>
    ///     Implicit conversion to string
    /// </summary>
    /// <param name="error"></param>
    /// <returns></returns>
    public static implicit operator string(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return error.Code;
    }

    /// <summary>
    ///     Equality operator
    /// </summary>
    /// <param name="a"></param>
    /// <param name="b"></param>
    /// <returns></returns>
    public static bool operator ==(Error? a, Error? b)
    {
        if (a is null && b is null)
        {
            return true;
        }

        if (a is null || b is null)
        {
            return false;
        }

        return a.Equals(b);
    }

    /// <summary>
    ///     Inequality operator
    /// </summary>
    /// <param name="a"></param>
    /// <param name="b"></param>
    /// <returns></returns>
    public static bool operator !=(Error? a, Error? b)
    {
        return !(a == b);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is Error error && Equals(error);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(Code, Message);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Code;
    }
}
