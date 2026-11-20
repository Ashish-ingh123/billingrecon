using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payers;

namespace BillingRecon.Application.Payers;

/// <summary>Reads one payer. Another tenant's payer is not found, exactly like an id nobody has (BR-002).</summary>
public sealed class GetPayerHandler(IPayerReadService payers) : IQueryHandler<GetPayerQuery, PayerDto>
{
    /// <inheritdoc />
    public async Task<Result<PayerDto>> HandleAsync(GetPayerQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var payer = await payers.GetAsync(query.Id, ct);

        return payer is null ? PayersErrors.PayerNotFound() : payer;
    }
}
