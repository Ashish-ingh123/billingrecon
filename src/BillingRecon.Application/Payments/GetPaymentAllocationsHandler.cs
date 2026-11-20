using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;

namespace BillingRecon.Application.Payments;

/// <summary>Reads one payment with its allocations. A payment of another tenant is not found, like an id nobody has.</summary>
public sealed class GetPaymentAllocationsHandler(IPaymentReadService payments) : IQueryHandler<GetPaymentAllocationsQuery, PaymentAllocationsDto>
{
    /// <inheritdoc />
    public async Task<Result<PaymentAllocationsDto>> HandleAsync(GetPaymentAllocationsQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var payment = await payments.GetWithAllocationsAsync(query.Id, ct);

        return payment is null ? PaymentsErrors.PaymentNotFound() : payment;
    }
}
