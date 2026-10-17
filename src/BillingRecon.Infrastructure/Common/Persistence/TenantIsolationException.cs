namespace BillingRecon.Infrastructure.Common.Persistence;

/// <summary>
/// Thrown when code tries to read or write tenant-scoped data for another tenant, or without a tenant (BR-002,
/// ADR 0009). It is always a defect, never a business failure: the API answers 500. It derives from
/// <see cref="Exception"/> directly, so that a handler written for some other failure (an
/// <see cref="InvalidOperationException"/>, a database error) cannot swallow it by accident. The message names
/// entity types and ids only, never data.
/// </summary>
public sealed class TenantIsolationException : Exception
{
    /// <summary>Creates the exception without a message.</summary>
    public TenantIsolationException()
    {
    }

    /// <summary>Creates the exception with a message that says which entity and which tenants were involved.</summary>
    public TenantIsolationException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and the exception that caused it.</summary>
    public TenantIsolationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
