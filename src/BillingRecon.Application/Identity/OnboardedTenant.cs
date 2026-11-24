using System.Text;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Identity;

/// <summary>
/// What onboarding made (ADR 0052): the tenant, its administrator and the one-time password, which the command shows
/// once and nothing stores or logs. The administrator must change it at the first sign-in (ADR 0051).
/// </summary>
public sealed record OnboardedTenant(TenantId TenantId, UserId AdminUserId, string OneTimePassword)
{
    // The password never reaches a log through ToString (backlog B24, closed by ADR 0051).
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("TenantId = ").Append(TenantId).Append(", AdminUserId = ").Append(AdminUserId).Append(", OneTimePassword = ***");
        return true;
    }
}
