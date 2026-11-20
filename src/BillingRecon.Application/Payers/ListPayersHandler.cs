using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Payers;

/// <summary>Lists the tenant's payers one page at a time, by name.</summary>
public sealed class ListPayersHandler(IValidator<ListPayersQuery> validator, IPayerReadService payers)
    : IQueryHandler<ListPayersQuery, PageOf<PayerDto>>
{
    /// <inheritdoc />
    public async Task<Result<PageOf<PayerDto>>> HandleAsync(ListPayersQuery query, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(query, ct) is { } invalid)
        {
            return invalid;
        }

        return await payers.ListAsync(query, ct);
    }
}
