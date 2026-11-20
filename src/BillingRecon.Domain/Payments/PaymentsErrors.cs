using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Payments;

/// <summary>The failures of the Payments module. The codes are part of the API contract (docs/api.md).</summary>
public static class PaymentsErrors
{
    public const string PaymentNotFoundCode = "payment_not_found";
    public const string PaymentDateInFutureCode = "payment_date_in_future";
    public const string PaymentDateTooOldCode = "payment_date_too_old";

    /// <summary>No payment with that id exists for the tenant. Another tenant's payment gets the same answer.</summary>
    public static Error PaymentNotFound() => Error.NotFound(PaymentNotFoundCode, "The payment does not exist.");

    /// <summary>The payment date is after today in India (BR-205).</summary>
    public static Error PaymentDateInFuture() => Error.BusinessRule(PaymentDateInFutureCode, "The payment date cannot be in the future (BR-205).");

    /// <summary>The payment date is earlier than the tenant allows a document to be back-dated (BR-205).</summary>
    public static Error PaymentDateTooOld(DateOnly earliest)
        => Error.BusinessRule(PaymentDateTooOldCode, $"The payment date cannot be earlier than {earliest:yyyy-MM-dd}, the earliest date this hospital allows (BR-205).");
}
