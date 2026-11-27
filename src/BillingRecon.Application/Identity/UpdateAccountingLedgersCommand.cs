using BillingRecon.Domain.Identity;

namespace BillingRecon.Application.Identity;

/// <summary>Saves the hospital's names for its accountant's ledgers under the version they were read with.</summary>
public sealed record UpdateAccountingLedgersCommand
{
    /// <summary>The names; an empty one takes a name away.</summary>
    public required AccountingLedgerNames Names { get; init; }

    /// <summary>The version of <c>If-Match</c>.</summary>
    public required string ExpectedVersion { get; init; }
}
