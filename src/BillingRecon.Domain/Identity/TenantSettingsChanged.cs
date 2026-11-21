using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Identity;

/// <summary>A TenantAdmin changed the tenant's settings (BR-607); the audit record holds the row before and after.</summary>
public sealed record TenantSettingsChanged(Money? PostingApprovalThreshold, int MaxBackdateDays, int DefaultDueDays) : IDomainEvent;
