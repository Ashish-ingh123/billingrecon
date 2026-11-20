using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;

namespace BillingRecon.Application.Payments;

/// <summary>Reads one payment. A payment of another tenant is not found, like an id nobody has.</summary>
public sealed class GetPaymentHandler(IPaymentReadService payments) : IQueryHandler<GetPaymentQuery, PaymentDto>
{
    /// <inheritdoc />
    public async Task<Result<PaymentDto>> HandleAsync(GetPaymentQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var payment = await payments.GetAsync(query.Id, ct);

        return payment is null ? PaymentsErrors.PaymentNotFound() : payment;
    }
}
