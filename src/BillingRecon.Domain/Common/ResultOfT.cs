namespace BillingRecon.Domain.Common;

/// <summary>A <see cref="Result"/> that carries a value when it succeeds.</summary>
/// <typeparam name="T">The type of the value.</typeparam>
public sealed class Result<T> : Result
{
    private readonly T? _value;

    internal Result(T value)
        : base(null)
    {
        ArgumentNullException.ThrowIfNull(value);

        _value = value;
    }

    internal Result(Error error)
        : base(error ?? throw new ArgumentNullException(nameof(error)))
    {
    }

    /// <summary>The value. Reading it on a failure is a bug and throws.</summary>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"A failed result has no value (error '{Error.Code}'). Check IsSuccess first.");

    /// <summary>Lets a method that returns <see cref="Result{T}"/> simply <c>return value;</c>.</summary>
    public static implicit operator Result<T>(T value) => new(value);

    /// <summary>Lets a method that returns <see cref="Result{T}"/> simply <c>return error;</c>.</summary>
    public static implicit operator Result<T>(Error error) => new(error);
}
