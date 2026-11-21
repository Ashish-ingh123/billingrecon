using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Identity;

/// <summary>One prefix change of the current tenant.</summary>
public sealed record GetPrefixChangeQuery(PrefixChangeId Id);
