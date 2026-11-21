using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;

namespace BillingRecon.Application.Identity;

/// <summary>The settings of a tenant and its prefix changes as the API answers them.</summary>
public static class SettingsMapping
{
    /// <summary>The settings of <paramref name="tenant"/>, with its version.</summary>
    public static TenantSettingsDto ToSettingsDto(this Tenant tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        return new TenantSettingsDto(
            PostingApprovalThreshold: tenant.PostingApprovalThreshold?.Amount,
            MaxBackdateDays: tenant.MaxBackdateDays,
            DefaultDueDays: tenant.DefaultDueDays,
            Prefixes: new DocumentPrefixesDto(
                Bill: tenant.PrefixOf(DocumentType.Bill),
                CreditNote: tenant.PrefixOf(DocumentType.CreditNote),
                Payment: tenant.PrefixOf(DocumentType.Payment),
                Adjustment: tenant.PrefixOf(DocumentType.Adjustment),
                Refund: tenant.PrefixOf(DocumentType.Refund)),
            Version: RowVersionText.Of(tenant));
    }

    /// <summary><paramref name="change"/>, with its version.</summary>
    public static PrefixChangeDto ToDto(this PrefixChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        return new PrefixChangeDto(
            Id: change.Id.Value,
            DocumentType: change.DocumentType,
            FromPrefix: change.FromPrefix,
            ToPrefix: change.ToPrefix,
            Status: change.Status,
            RequestedAtUtc: change.CreatedAtUtc,
            RequestedByUserId: change.CreatedByUserId?.Value,
            DecidedAtUtc: change.DecidedAtUtc,
            Version: RowVersionText.Of(change));
    }
}
