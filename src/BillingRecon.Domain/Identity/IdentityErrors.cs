using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Identity;

/// <summary>
/// The business failures of the Identity module: one code constant and one factory per failure, so that a code is
/// written once, cited from the tests by name, and listed in the API contract (docs/api.md). Every module keeps its
/// own <c>&lt;Module&gt;Errors</c> class in this shape; a test checks that the codes are snake_case and unique.
/// </summary>
public static class IdentityErrors
{
    /// <summary>Code of <see cref="TenantInvalid"/>.</summary>
    public const string TenantInvalidCode = "tenant_invalid";

    /// <summary>The code or the name of a tenant is not valid; <paramref name="fieldErrors"/> says which, per field.</summary>
    public static Error TenantInvalid(IReadOnlyDictionary<string, string[]> fieldErrors)
        => Error.Validation(TenantInvalidCode, "The tenant is not valid.", fieldErrors);
}
