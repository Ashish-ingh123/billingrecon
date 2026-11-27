using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Identity;

/// <summary>Answers the names, or none with the version of a row that is not there.</summary>
public sealed class GetAccountingLedgersHandler(IAccountingLedgersRepository ledgers) : IQueryHandler<GetAccountingLedgersQuery, AccountingLedgersDto>
{
    /// <inheritdoc />
    public async Task<Result<AccountingLedgersDto>> HandleAsync(GetAccountingLedgersQuery query, CancellationToken ct)
        => (await ledgers.FindAsync(ct))?.ToDto() ?? AccountingLedgersMapping.NoneDto();
}
