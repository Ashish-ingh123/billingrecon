using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Payers;

/// <summary>
/// The patients and the payers a text names, as queries another module's list puts into its own (Prompt 11.1; ADR
/// 0036, amended 2026-11-26): the search box of a list of documents finds a document by its patient or its payer, and
/// the documents' modules know the parties only through this contract. Nothing is read here; the tenant filter applies
/// when the list that uses a query is read. The text is data, never a pattern (ADR 0018).
/// </summary>
public interface IPartySearch
{
    /// <summary>The patients whose name or MRN holds the text.</summary>
    /// <param name="text">The text of the search, as typed.</param>
    /// <returns>Their ids, as a query.</returns>
    public IQueryable<PatientId> PatientsNamed(string text);

    /// <summary>The payers whose name holds the text, and the payers of the patients whose name or MRN does.</summary>
    /// <param name="text">The text of the search, as typed.</param>
    /// <returns>Their ids, as a query.</returns>
    public IQueryable<PayerId> PayersNamed(string text);
}
