using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Identity;

/// <summary>
/// Reads the hospital's letterhead (ADR 0054), for anyone who is signed in: whoever may print a document needs its
/// head and foot, and it says nothing the printed page does not.
/// </summary>
public sealed class GetLetterheadHandler(ITenantRepository tenants) : IQueryHandler<GetLetterheadQuery, LetterheadDto>
{
    /// <inheritdoc />
    public async Task<Result<LetterheadDto>> HandleAsync(GetLetterheadQuery query, CancellationToken ct)
        => (await tenants.GetCurrentAsync(ct)).ToLetterheadDto();
}
