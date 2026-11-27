using System.Security.Claims;
using BillingRecon.Api.Common.Authorization;
using BillingRecon.Domain.Common;
using Microsoft.AspNetCore.Authorization;

namespace BillingRecon.Api.Reporting;

/// <summary>
/// Which waiting requests a caller is shown by the pending-approvals report and by the panel of the dashboard (review
/// 2026-11, finding R8): a row names the document, its payer and its amount, so it is shown to whoever may read that
/// kind of document, and to whoever may decide. Until 2026-11-26 every role was shown every row, and with it documents
/// its role cannot open. A kind of request that is not of a document a role reads is the deciders' alone.
/// </summary>
public static class PendingApprovalPolicies
{
    /// <summary>The policy that reads the requests of each kind of document.</summary>
    public static IReadOnlyDictionary<ApprovalDocumentType, string> ByDocumentType { get; } = new Dictionary<ApprovalDocumentType, string>
    {
        [ApprovalDocumentType.Bill] = Policies.ReadBills,
        [ApprovalDocumentType.CreditNote] = Policies.ReadCreditNotes,
        [ApprovalDocumentType.CreditNoteAllocation] = Policies.ReadCreditNotes,
        [ApprovalDocumentType.Adjustment] = Policies.ReadAdjustments,
        [ApprovalDocumentType.Refund] = Policies.ReadRefunds,
        [ApprovalDocumentType.Payment] = Policies.ReadPayments,
        [ApprovalDocumentType.PaymentAllocation] = Policies.ReadPayments,
        [ApprovalDocumentType.PrefixChange] = Policies.Decide,
        [ApprovalDocumentType.OpeningBatch] = Policies.Decide,
        [ApprovalDocumentType.CashierShift] = Policies.Decide,
    };

    /// <summary>The kinds of document whose waiting requests the caller is shown; a kind that is not named is shown to no one.</summary>
    /// <param name="authorization">Decides each policy for the caller.</param>
    /// <param name="user">The caller.</param>
    /// <returns>The kinds, in the order of the enumeration.</returns>
    public static async Task<IReadOnlyCollection<ApprovalDocumentType>> VisibleToAsync(IAuthorizationService authorization, ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(authorization);

        var decides = (await authorization.AuthorizeAsync(user, Policies.Decide)).Succeeded;
        var kinds = ByDocumentType.OrderBy(pair => pair.Key).ToArray();
        var shown = new bool[kinds.Length];

        for (var index = 0; index < kinds.Length; index++)
        {
            shown[index] = decides || (await authorization.AuthorizeAsync(user, kinds[index].Value)).Succeeded;
        }

        return [.. kinds.Where((_, index) => shown[index]).Select(pair => pair.Key)];
    }
}
