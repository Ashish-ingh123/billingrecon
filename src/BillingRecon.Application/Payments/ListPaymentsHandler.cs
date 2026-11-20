using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Payments;

/// <summary>Lists the payments of the tenant one page at a time, the newest payment date first.</summary>
public sealed class ListPaymentsHandler(IValidator<ListPaymentsQuery> validator, IPaymentReadService payments)
    : IQueryHandler<ListPaymentsQuery, PageOf<PaymentDto>>
{
    /// <inheritdoc />
    public async Task<Result<PageOf<PaymentDto>>> HandleAsync(ListPaymentsQuery query, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(query, ct) is { } invalid)
        {
            return invalid;
        }

        return await payments.ListAsync(query, ct);
    }
}
