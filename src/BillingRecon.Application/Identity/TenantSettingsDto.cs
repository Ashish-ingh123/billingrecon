namespace BillingRecon.Application.Identity;

/// <summary>
/// The settings of the tenant (BR-607; docs/api.md section 8.13): the approval threshold (null for none), the back-date
/// window and the default due days, the default prefix of each document type, and the tenant's version.
/// </summary>
public sealed record TenantSettingsDto(
    decimal? PostingApprovalThreshold,
    int MaxBackdateDays,
    int DefaultDueDays,
    DocumentPrefixesDto Prefixes,
    LetterheadDto Letterhead,
    string Version);
