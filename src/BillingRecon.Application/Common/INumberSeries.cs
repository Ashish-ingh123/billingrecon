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
/// The tenant is the tenant of the current scope and is never passed in. A use case takes at most one number, so two
/// series are never locked in one transaction.
/// </para>
/// </summary>
public interface INumberSeries
{
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
