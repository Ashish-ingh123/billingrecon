using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Receivables;

/// <summary>
/// The cash a shift moved, as the ledger has it (BR-312; ADR 0060): the Cash entries of the postings that name the
/// shift. <paramref name="Received"/> is their debits (cash payments recorded), <paramref name="PaidOut"/> their
/// credits (cash refunds, and cash payments reversed while the shift was open).
/// </summary>
/// <param name="Received">The debits to Cash.</param>
/// <param name="PaidOut">The credits to Cash.</param>
public sealed record ShiftCash(Money Received, Money PaidOut)
{
    /// <summary>A shift that moved no cash.</summary>
    public static ShiftCash None { get; } = new(Money.Zero, Money.Zero);

    /// <summary>Received less paid out: what the shift added to its drawer.</summary>
    public Money Movement => Received - PaidOut;
}
