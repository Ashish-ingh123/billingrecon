namespace BillingRecon.Application.Identity;

/// <summary>
/// The new settings of the current tenant (BR-607, US-03), at the version the TenantAdmin read: the approval threshold
/// (null for none), the back-date window and the default due days. The prefixes are not here: they change only through
/// a prefix change (ADR 0033).
/// </summary>
public sealed record UpdateTenantSettingsCommand(decimal? PostingApprovalThreshold, int? MaxBackdateDays, int? DefaultDueDays, string ExpectedVersion);
