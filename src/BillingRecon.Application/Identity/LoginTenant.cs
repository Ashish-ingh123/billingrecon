using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;

namespace BillingRecon.Application.Identity;

/// <summary>What a sign-in knows of the tenant it names: its id and whether it is active.</summary>
public sealed record LoginTenant(TenantId Id, TenantStatus Status);
