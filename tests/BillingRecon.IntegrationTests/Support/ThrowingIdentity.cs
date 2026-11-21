using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;

namespace BillingRecon.IntegrationTests.Support;

/// <summary>An identity that throws when it is asked for the tenant, the user or the roles: a test fails if the code under test asks.</summary>
public sealed class ThrowingIdentity : ITenantContext, ICurrentUser
{
    /// <summary>The message of what is thrown.</summary>
    public const string Message = "The identity was asked for, which this test does not allow.";

    /// <summary>Always throws.</summary>
    public TenantId? TenantId => throw new InvalidOperationException(Message);

    /// <summary>Always throws.</summary>
    public UserId? UserId => throw new InvalidOperationException(Message);

    /// <summary>Always throws.</summary>
    public IReadOnlyCollection<Role> Roles => throw new InvalidOperationException(Message);
}
