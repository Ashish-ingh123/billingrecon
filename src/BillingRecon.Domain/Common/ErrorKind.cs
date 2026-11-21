namespace BillingRecon.Domain.Common;

/// <summary>
/// The category of a business failure. The API maps each kind to exactly one HTTP status (CLAUDE.md section 5),
/// so choosing the kind is choosing the status.
/// </summary>
public enum ErrorKind
{
    /// <summary>The request is malformed or a field is invalid (400).</summary>
    Validation,

    /// <summary>The addressed resource does not exist for this tenant (404).</summary>
    NotFound,

    /// <summary>The caller is known but not allowed to do this (403).</summary>
    Forbidden,

    /// <summary>The request conflicts with the current state, for example a stale version (409).</summary>
    Conflict,

    /// <summary>The request is well-formed but a business rule forbids it (422).</summary>
    BusinessRule,

    /// <summary>The caller could not be authenticated, for example a refused sign-in (401; ADR 0031).</summary>
    Unauthorized,
}
