using BillingRecon.Api.Common.Authorization;
using BillingRecon.Domain.Adjustments;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using BillingRecon.Domain.Payers;
using BillingRecon.Domain.Payments;
using BillingRecon.Domain.Receivables;

namespace BillingRecon.Api.Audit;

/// <summary>
/// Who reads the audit trail of a record: whoever may read the record (review 2026-11, finding R7; ADR 0032, amended
/// 2026-11-26). A trail holds the record before and after each change, whole, so it is the record: each kind of
/// audited record is named here with the read policy of its document, by the name the audit gives it (the class name).
/// A kind that is not named has no reader: a new audited record is closed until its line is added here, and an
/// architecture test fails until it is.
/// </summary>
public static class AuditTrailPolicies
{
    /// <summary>The read policy of each kind of audited record.</summary>
    public static IReadOnlyDictionary<string, string> ByEntityType { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [nameof(Patient)] = Policies.ReadReferenceData,
        [nameof(Payer)] = Policies.ReadReferenceData,
        [nameof(ServiceItem)] = Policies.ReadReferenceData,
        [nameof(Bill)] = Policies.ReadBills,
        [nameof(CreditNote)] = Policies.ReadCreditNotes,
        [nameof(Adjustment)] = Policies.ReadAdjustments,
        [nameof(Payment)] = Policies.ReadPayments,
        [nameof(Refund)] = Policies.ReadRefunds,

        // A shift is read in full by who reads the day-end report; a cashier reads the shift itself, not its trail (ADR 0060).
        [nameof(CashierShift)] = Policies.DayEnd,

        // What a checker decides, and the batch of opening balances, are read by who may decide.
        [nameof(ApprovalRequest)] = Policies.Decide,
        [nameof(OpeningBatch)] = Policies.Decide,

        // The users and the settings are the TenantAdmin's alone (ADR 0032 point 6, ADR 0033 point 9).
        [nameof(User)] = Policies.ManageTenant,
        [nameof(Tenant)] = Policies.ManageTenant,
        [nameof(PrefixChange)] = Policies.ManageTenant,
        [nameof(NumberSeries)] = Policies.ManageTenant,
        [nameof(PrintSettings)] = Policies.ManageTenant,
        [nameof(AccountingLedgers)] = Policies.ManageTenant,
    };
}
