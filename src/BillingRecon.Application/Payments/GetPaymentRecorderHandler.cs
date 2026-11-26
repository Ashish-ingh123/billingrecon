using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;

namespace BillingRecon.Application.Payments;

/// <summary>
/// Reads who recorded a payment, by name, for its receipt (ADR 0059). A read of its own, so that the payment answers
/// the same wherever it is read; the name is asked of Identity, which owns the users, and nothing else of the user is
/// answered. Another hospital's payment is not found.
/// </summary>
public sealed class GetPaymentRecorderHandler(IPaymentReadService payments, IUserNames userNames)
    : IQueryHandler<GetPaymentRecorderQuery, DocumentRecorderDto>
{
    /// <inheritdoc />
    public async Task<Result<DocumentRecorderDto>> HandleAsync(GetPaymentRecorderQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (await payments.GetAsync(query.Id, ct) is null)
        {
            return PaymentsErrors.PaymentNotFound();
        }

        var recorder = await payments.FindRecorderAsync(query.Id, ct);

        return new DocumentRecorderDto(recorder is null ? null : await userNames.FindDisplayNameAsync(recorder.Value, ct));
    }
}
