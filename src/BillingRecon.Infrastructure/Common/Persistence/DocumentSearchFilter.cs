using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Common.Persistence;

/// <summary>
/// What the search of a list of documents asks the database (Prompt 11.1; ADR 0036, amended 2026-11-26): the pattern
/// for the number, the patients and the payers the text names, as queries a list puts into its own, and the amount the
/// text states. The text is data, never a pattern (ADR 0018). The patients and the payers come from the module that
/// has them (IPartySearch), as queries: this class knows their ids and nothing else of them.
/// </summary>
internal sealed class DocumentSearchFilter
{
    private DocumentSearchFilter(string pattern, IQueryable<PatientId> patients, IQueryable<PayerId> payers, Money? amount)
    {
        Pattern = pattern;
        Patients = patients;
        Payers = payers;
        Amount = amount;
    }

    /// <summary>The LIKE pattern that finds the text anywhere in a column.</summary>
    public string Pattern { get; }

    /// <summary>The patients whose name or MRN holds the text.</summary>
    public IQueryable<PatientId> Patients { get; }

    /// <summary>The payers whose name holds the text, and the payers of the patients whose name or MRN does.</summary>
    public IQueryable<PayerId> Payers { get; }

    /// <summary>The amount the text states, when it reads as one.</summary>
    public Money? Amount { get; }

    /// <summary>
    /// How many documents a search may find for them to be read by their ids. Measured on 100,000 payments (LocalDB,
    /// 2026-11-26): the page of a search that finds few documents took over a second when SQL Server walked the list
    /// in its order and looked at each document for a match, and a few milliseconds when it was given the ids.
    /// </summary>
    public const int FewEnoughToName = 500;

    /// <summary>
    /// Narrows a list to what its search found. When that is at most <see cref="FewEnoughToName"/> documents, their
    /// ids are read once and the list becomes those documents, with their count known; when it is more, the list
    /// stays the query it was, whose matches are dense enough to be found in its order.
    /// </summary>
    /// <typeparam name="TDocument">The document of the list.</typeparam>
    /// <typeparam name="TId">The type of its id.</typeparam>
    /// <param name="all">The documents of the list with nothing filtered, as the page reads them.</param>
    /// <param name="foundIds">The ids of the documents the filters and the search leave: the parts of the search, joined.</param>
    /// <param name="found">The documents of those ids, as a query.</param>
    /// <param name="id">The id of a document.</param>
    /// <param name="ct">Cancels the read.</param>
    /// <returns>The list to page, and its count.</returns>
    public static async Task<(IQueryable<TDocument> Documents, int Count)> NarrowAsync<TDocument, TId>(
        IQueryable<TDocument> all,
        IQueryable<TId> foundIds,
        IQueryable<TDocument> found,
        System.Linq.Expressions.Expression<Func<TDocument, TId>> id,
        CancellationToken ct)
    {
        // The ids are asked of the joined parts themselves, and so is their count: asked of the documents "whose id is
        // among them", the same question took five times as long (measured with the rest).
        var ids = await foundIds.Take(FewEnoughToName + 1).ToListAsync(ct);
        if (ids.Count > FewEnoughToName)
        {
            return (found, await foundIds.CountAsync(ct));
        }

        var contains = System.Linq.Expressions.Expression.Call(
            typeof(Enumerable), nameof(Enumerable.Contains), [typeof(TId)], System.Linq.Expressions.Expression.Constant(ids), id.Body);

        return (all.Where(System.Linq.Expressions.Expression.Lambda<Func<TDocument, bool>>(contains, id.Parameters)), ids.Count);
    }

    /// <summary>The filter of a search, or null when the text is missing or too short to filter.</summary>
    /// <param name="search">The text as the request gave it.</param>
    /// <param name="patientsNamed">The patients a text names, as a query.</param>
    /// <param name="payersNamed">The payers a text names, as a query.</param>
    /// <returns>The filter, or null for a list without a search.</returns>
    public static DocumentSearchFilter? For(string? search, Func<string, IQueryable<PatientId>> patientsNamed, Func<string, IQueryable<PayerId>> payersNamed)
    {
        ArgumentNullException.ThrowIfNull(patientsNamed);
        ArgumentNullException.ThrowIfNull(payersNamed);

        return DocumentSearch.Term(search) is { } term
            ? new DocumentSearchFilter(LikePattern.Containing(term), patientsNamed(term), payersNamed(term), DocumentSearch.Amount(term))
            : null;
    }
}
