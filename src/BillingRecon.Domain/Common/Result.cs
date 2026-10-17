namespace BillingRecon.Domain.Common;

/// <summary>
/// The outcome of an operation that can fail for an expected business reason. A failure carries an
/// <see cref="Common.Error"/>; bugs and infrastructure faults are exceptions and never travel in a result
/// (CLAUDE.md section 5).
/// </summary>
public class Result
{
    private static readonly Result Succeeded = new(null);

    private readonly Error? _error;

    private protected Result(Error? error) => _error = error;

    /// <summary>True when the operation did what was asked.</summary>
    public bool IsSuccess => _error is null;

    /// <summary>True when a business rule stopped the operation; <see cref="Error"/> says which.</summary>
    public bool IsFailure => _error is not null;

    /// <summary>The failure. Reading it on a success is a bug and throws.</summary>
    public Error Error => _error ?? throw new InvalidOperationException("A successful result has no error. Check IsFailure first.");

    /// <summary>A success without a value.</summary>
    public static Result Success() => Succeeded;

    /// <summary>A failure.</summary>
    public static Result Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return new Result(error);
    }

    /// <summary>A success carrying <paramref name="value"/>.</summary>
    public static Result<T> Success<T>(T value) => new(value);

    /// <summary>A failure of an operation that would have produced a <typeparamref name="T"/>.</summary>
    public static Result<T> Failure<T>(Error error) => new(error);

    /// <summary>Lets a method that returns <see cref="Result"/> simply <c>return error;</c>.</summary>
    public static implicit operator Result(Error error) => Failure(error);
}
