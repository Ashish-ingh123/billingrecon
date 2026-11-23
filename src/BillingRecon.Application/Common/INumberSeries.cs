using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Common;

/// <summary>
/// Hands out the gapless numbers of posted documents (BR-801, BR-802; ADR 0008). A posting use case calls it after all
/// validation, as its last step before it writes the document, and then commits its unit of work.
/// <para>
/// The number is taken inside the transaction of the use case: the call opens that transaction if none is open, and
/// <see cref="IUnitOfWork.CommitAsync"/> commits it together with everything the use case saved. If the use case fails
/// after taking a number, or never commits, the transaction rolls back and the number is free again: the next posting
/// gets it, and the series has no gap. Until the commit, other postings of the same series wait, so the time between
/// this call and the commit is kept short.
/// </para>
/// <para>
/// The tenant is the tenant of the current scope and is never passed in. A use case takes at most one number, or holds
/// one series to change its prefix, so two series are never locked in one transaction.
/// </para>
/// </summary>
public interface INumberSeries
{
    /// <summary>
    /// Holds the current tenant's series for <paramref name="type"/> in <paramref name="fiscalYear"/>, whether it exists
    /// yet or not, under the lock <see cref="NextAsync"/> takes, until the transaction of the use case ends: meanwhile no
    /// number is taken from it and nobody creates it. For an approved prefix change (ADR 0033, built note point 5): held
    /// before the tenant and the series are read, a first number of the year cannot be created from the old default in
    /// the instant of the approval. Opens the transaction as <see cref="NextAsync"/> does; takes no number.
    /// </summary>
    /// <exception cref="ArgumentException">The type is not one the enum defines, or the fiscal year is the default value.</exception>
    /// <exception cref="InvalidOperationException">The current scope has no tenant: a fault, not a business failure.</exception>
    public Task HoldAsync(DocumentType type, FiscalYear fiscalYear, CancellationToken ct);

    /// <summary>
    /// The next number of the current tenant's series for <paramref name="type"/> in <paramref name="fiscalYear"/>,
    /// which is created on first use with the tenant's default prefix for that type.
    /// </summary>
    /// <exception cref="ArgumentException">The type is not one the enum defines, or the fiscal year is the default value.</exception>
    /// <exception cref="InvalidOperationException">
    /// The current scope has no tenant or its row does not exist, or the series is exhausted: its next number would be
    /// longer than sixteen characters. All three are faults, not business failures.
    /// </exception>
    public Task<DocumentNumber> NextAsync(DocumentType type, FiscalYear fiscalYear, CancellationToken ct);
}
