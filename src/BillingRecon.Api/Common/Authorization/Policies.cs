using BillingRecon.Domain.Identity;

namespace BillingRecon.Api.Common.Authorization;

/// <summary>
/// The named policies of the API, one per capability of the permission matrix (docs/requirements.md section 1;
/// ADR 0032), and the roles each admits. Every action names exactly one of them, on the action itself; a user with
/// several roles may do what any of them may. A blank cell of the matrix is no access, not even reading, except that a
/// Cashier reads credit notes (ADR 0032, point 3).
/// </summary>
public static class Policies
{
    /// <summary>The users, the tenant's settings, and asking for a prefix change: TenantAdmin (ADR 0033).</summary>
    public const string ManageTenant = nameof(ManageTenant);

    /// <summary>Reading patients, payers and service items: every role.</summary>
    public const string ReadReferenceData = nameof(ReadReferenceData);

    /// <summary>Registering and changing patients, payers and service items.</summary>
    public const string WriteReferenceData = nameof(WriteReferenceData);

    /// <summary>Reading bills: every role.</summary>
    public const string ReadBills = nameof(ReadBills);

    /// <summary>Drafting, editing, voiding and posting bills.</summary>
    public const string WriteBills = nameof(WriteBills);

    /// <summary>Reading credit notes: every role, the Cashier for refunds from credit (ADR 0032, point 3).</summary>
    public const string ReadCreditNotes = nameof(ReadCreditNotes);

    /// <summary>Reading adjustments.</summary>
    public const string ReadAdjustments = nameof(ReadAdjustments);

    /// <summary>Drafting, editing, voiding and submitting credit notes and adjustments, and applying a credit note's credit.</summary>
    public const string DraftCreditNotesAndAdjustments = nameof(DraftCreditNotesAndAdjustments);

    /// <summary>Reading payments and their allocations: every role.</summary>
    public const string ReadPayments = nameof(ReadPayments);

    /// <summary>Recording and allocating payments.</summary>
    public const string RecordPayments = nameof(RecordPayments);

    /// <summary>Reading refunds.</summary>
    public const string ReadRefunds = nameof(ReadRefunds);

    /// <summary>Drafting, editing, voiding and submitting refunds.</summary>
    public const string DraftRefunds = nameof(DraftRefunds);

    /// <summary>Asking for the reversal of a payment, an allocation or an application of credit (the maker).</summary>
    public const string RequestReversals = nameof(RequestReversals);

    /// <summary>Approving and rejecting, the checker's inbox, and reading the prefix changes they decide (BR-603, BR-605, ADR 0033).</summary>
    public const string Decide = nameof(Decide);

    /// <summary>Reading the audit trail: every role (US-43); the trail of a user is the TenantAdmin's alone.</summary>
    public const string ReadAudit = nameof(ReadAudit);

    /// <summary>The reports of BR-901: every role (the matrix's "Reports" row, ADR 0036).</summary>
    public const string Reports = nameof(Reports);

    /// <summary>The dashboard (US-44, BR-902): whoever runs or audits the hospital (the matrix's "Dashboard" row, ADR 0037).</summary>
    public const string Dashboard = nameof(Dashboard);

    /// <summary>Opening and closing a shift of one's own: whoever may record a payment (the matrix's shift row, ADR 0060).</summary>
    public const string WorkShift = nameof(WorkShift);

    /// <summary>
    /// Reading shifts: every role, a Cashier and a BillingClerk only the shifts they opened, which the use case sees to
    /// (ADR 0060).
    /// </summary>
    public const string ReadShifts = nameof(ReadShifts);

    /// <summary>The day-end report of every shift (US-48, BR-903): whoever runs or audits the hospital (ADR 0060).</summary>
    public const string DayEnd = nameof(DayEnd);

    /// <summary>
    /// The accountant's export (ADR 0062): whoever runs or audits the hospital, as for the day-end report. An
    /// accountant is given the role Viewer.
    /// </summary>
    public const string AccountingExport = nameof(AccountingExport);

    /// <summary>Any signed-in user, whatever the roles: the service information.</summary>
    public const string SignedIn = nameof(SignedIn);

    /// <summary>The roles each policy admits. <see cref="SignedIn"/> admits any signed-in user and is not here.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<Role>> Roles { get; } = new Dictionary<string, IReadOnlyList<Role>>(StringComparer.Ordinal)
    {
        [ManageTenant] = [Role.TenantAdmin],
        [ReadReferenceData] = [Role.TenantAdmin, Role.Supervisor, Role.BillingClerk, Role.Cashier, Role.Viewer, Role.Accountant],
        [WriteReferenceData] = [Role.TenantAdmin, Role.Supervisor, Role.BillingClerk],
        [ReadBills] = [Role.TenantAdmin, Role.Supervisor, Role.BillingClerk, Role.Cashier, Role.Viewer, Role.Accountant],
        [WriteBills] = [Role.TenantAdmin, Role.Supervisor, Role.BillingClerk],
        [ReadCreditNotes] = [Role.TenantAdmin, Role.Supervisor, Role.BillingClerk, Role.Cashier, Role.Viewer, Role.Accountant],
        [ReadAdjustments] = [Role.TenantAdmin, Role.Supervisor, Role.BillingClerk, Role.Viewer, Role.Accountant],
        [DraftCreditNotesAndAdjustments] = [Role.TenantAdmin, Role.Supervisor, Role.BillingClerk],
        [ReadPayments] = [Role.TenantAdmin, Role.Supervisor, Role.BillingClerk, Role.Cashier, Role.Viewer, Role.Accountant],
        [RecordPayments] = [Role.TenantAdmin, Role.Supervisor, Role.BillingClerk, Role.Cashier],
        [ReadRefunds] = [Role.TenantAdmin, Role.Supervisor, Role.Cashier, Role.Viewer, Role.Accountant],
        [DraftRefunds] = [Role.TenantAdmin, Role.Supervisor, Role.Cashier],
        [RequestReversals] = [Role.TenantAdmin, Role.Supervisor, Role.BillingClerk, Role.Cashier],
        [Decide] = [Role.TenantAdmin, Role.Supervisor],
        [ReadAudit] = [Role.TenantAdmin, Role.Supervisor, Role.BillingClerk, Role.Cashier, Role.Viewer, Role.Accountant],
        [Reports] = [Role.TenantAdmin, Role.Supervisor, Role.BillingClerk, Role.Cashier, Role.Viewer, Role.Accountant],
        [Dashboard] = [Role.TenantAdmin, Role.Supervisor, Role.Viewer, Role.Accountant],
        [WorkShift] = [Role.TenantAdmin, Role.Supervisor, Role.BillingClerk, Role.Cashier],
        [ReadShifts] = [Role.TenantAdmin, Role.Supervisor, Role.BillingClerk, Role.Cashier, Role.Viewer, Role.Accountant],
        [DayEnd] = [Role.TenantAdmin, Role.Supervisor, Role.Viewer, Role.Accountant],
        [AccountingExport] = [Role.TenantAdmin, Role.Supervisor, Role.Viewer, Role.Accountant],
    };

    /// <summary>Every policy of the catalogue, <see cref="SignedIn"/> included.</summary>
    public static IReadOnlyCollection<string> All { get; } = [.. Roles.Keys, SignedIn];
}
