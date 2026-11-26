using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;

namespace BillingRecon.Application.Payments;

/// <summary>
/// Reads who drafted a refund, by name, for its voucher (ADR 0059). A read of its own, so that the refund answers the
/// same wherever it is read; the name is asked of Identity, which owns the users. Another hospital's refund is not
/// found.
/// </summary>
public sealed class GetRefundRecorderHandler(IRefundReadService refunds, IUserNames userNames)
    : IQueryHandler<GetRefundRecorderQuery, DocumentRecorderDto>
{
    /// <inheritdoc />
    public async Task<Result<DocumentRecorderDto>> HandleAsync(GetRefundRecorderQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (await refunds.GetAsync(query.Id, ct) is null)
        {
            return PaymentsErrors.RefundNotFound();
        }

        var recorder = await refunds.FindRecorderAsync(query.Id, ct);

        return new DocumentRecorderDto(recorder is null ? null : await userNames.FindDisplayNameAsync(recorder.Value, ct));
    }
}
