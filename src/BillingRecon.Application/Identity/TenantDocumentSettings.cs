using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Identity;

/// <summary>
/// The settings of the current tenant that the dates of a document depend on (BR-205, BR-607): how many days a business
/// date may lie in the past, and how many days after its date a bill is due when nobody says otherwise.
/// </summary>
public sealed record TenantDocumentSettings(int MaxBackdateDays, int DefaultDueDays, Money? PostingApprovalThreshold = null);
