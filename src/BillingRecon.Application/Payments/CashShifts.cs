using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;

namespace BillingRecon.Application.Payments;

/// <summary>
/// How a use case that moves cash finds the shift whose drawer it moves it in or out of (BR-311; ADR 0060). Asked
/// before the number is taken, with everything else that can refuse (ADR 0022).
/// </summary>
internal static class CashShifts
{
    /// <summary>
    /// The open shift of <paramref name="cashier"/> for a cash document dated <paramref name="documentDate"/>:
    /// <c>shift_not_open</c> when the user is unknown or has none, <c>shift_date_mismatch</c> when the document is
    /// dated another day than the shift. <paramref name="ofDrafter"/> words the refusal for a checker who approves
    /// the refund of another user.
    /// </summary>
    public static async Task<Result<CashierShift>> RequireAsync(
        ICashierShiftRepository shifts,
        UserId? cashier,
        DateOnly documentDate,
        bool ofDrafter,
        CancellationToken ct)
    {
        if (cashier is not { } user || await shifts.FindOpenOfAsync(user, ct) is not { } shift)
        {
            return PaymentsErrors.ShiftNotOpen(ofDrafter);
        }

        var takes = shift.CheckTakes(documentDate);

        return takes.IsSuccess ? shift : takes.Error;
    }
}
