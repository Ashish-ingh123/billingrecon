using System.Collections.ObjectModel;

namespace BillingRecon.Domain.Common;

/// <summary>
/// An expected business failure: a stable code for machines, a message for people and the kind that decides the
/// HTTP status. It travels inside a <see cref="Result"/> and is never thrown (CLAUDE.md section 5).
/// </summary>
public sealed record Error
{
    private static readonly IReadOnlyDictionary<string, string[]> NoFieldErrors = ReadOnlyDictionary<string, string[]>.Empty;

    /// <summary>Creates an error.</summary>
    /// <param name="code">Stable snake_case identifier, for example <c>bill_already_posted</c>. It is part of the API contract.</param>
    /// <param name="message">What went wrong, in words a caller of the API can act on.</param>
    /// <param name="kind">The category, which decides the HTTP status.</param>
    public Error(string code, string message, ErrorKind kind)
    {
        if (!IsSnakeCase(code))
        {
            throw new ArgumentException($"Error code '{code}' must be snake_case, for example 'bill_already_posted'.", nameof(code));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown error kind.");
        }

        Code = code;
        Message = message;
        Kind = kind;
    }

    /// <summary>Stable snake_case identifier; clients branch on it.</summary>
    public string Code { get; }

    /// <summary>Human-readable explanation.</summary>
    public string Message { get; }

    /// <summary>The category, which decides the HTTP status.</summary>
    public ErrorKind Kind { get; }

    /// <summary>Messages per field, keyed by field name. Only a validation error carries any.</summary>
    public IReadOnlyDictionary<string, string[]> FieldErrors { get; private init; } = NoFieldErrors;

    /// <summary>A malformed request or invalid field (400), optionally with messages per field.</summary>
    public static Error Validation(string code, string message, IReadOnlyDictionary<string, string[]>? fieldErrors = null)
        => new(code, message, ErrorKind.Validation) { FieldErrors = Copy(fieldErrors) };

    /// <summary>The addressed resource does not exist for this tenant (404).</summary>
    public static Error NotFound(string code, string message) => new(code, message, ErrorKind.NotFound);

    /// <summary>The caller is known but not allowed to do this (403).</summary>
    public static Error Forbidden(string code, string message) => new(code, message, ErrorKind.Forbidden);

    /// <summary>The request conflicts with the current state (409).</summary>
    public static Error Conflict(string code, string message) => new(code, message, ErrorKind.Conflict);

    /// <summary>A business rule forbids the request (422).</summary>
    public static Error BusinessRule(string code, string message) => new(code, message, ErrorKind.BusinessRule);

    /// <summary>The caller could not be authenticated (401).</summary>
    public static Error Unauthorized(string code, string message) => new(code, message, ErrorKind.Unauthorized);

    /// <summary>Two errors are equal when code, message, kind and the content of the field errors are equal.</summary>
    public bool Equals(Error? other)
        => other is not null
            && Code == other.Code
            && Message == other.Message
            && Kind == other.Kind
            && SameFieldErrors(FieldErrors, other.FieldErrors);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Code, Message, Kind);

    private static bool IsSnakeCase(string? code)
    {
        if (string.IsNullOrEmpty(code) || code[0] is < 'a' or > 'z')
        {
            return false;
        }

        var previousWasUnderscore = false;
        foreach (var character in code)
        {
            if (character == '_')
            {
                if (previousWasUnderscore)
                {
                    return false;
                }

                previousWasUnderscore = true;
            }
            else if (character is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
            {
                previousWasUnderscore = false;
            }
            else
            {
                return false;
            }
        }

        return !previousWasUnderscore;
    }

    // The error owns its field errors: later changes to the caller's dictionary or arrays must not reach it.
    private static IReadOnlyDictionary<string, string[]> Copy(IReadOnlyDictionary<string, string[]>? source)
    {
        if (source is null || source.Count == 0)
        {
            return NoFieldErrors;
        }

        var copy = new Dictionary<string, string[]>(source.Count, StringComparer.Ordinal);
        foreach (var (field, messages) in source)
        {
            copy[field] = [.. messages];
        }

        return new ReadOnlyDictionary<string, string[]>(copy);
    }

    private static bool SameFieldErrors(IReadOnlyDictionary<string, string[]> left, IReadOnlyDictionary<string, string[]> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var (field, messages) in left)
        {
            if (!right.TryGetValue(field, out var otherMessages) || !messages.AsSpan().SequenceEqual(otherMessages))
            {
                return false;
            }
        }

        return true;
    }
}
