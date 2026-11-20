using BillingRecon.Domain.Common;
using BillingRecon.Domain.Receivables;

namespace BillingRecon.Application.Receivables;

/// <summary>
/// What a business module hands to <see cref="IReceivablesLedger"/>: one financial event, as one of the posting
/// templates of docs/database.md section 5.3. Each event gets a named factory here with the slice that builds its
/// document (the first is the bill posting of Prompt 3.5), so that a module never spells out accounts or sides
/// itself and every document of a kind is posted the same way. The constructor is public for the ledger's own tests
/// and for those factories; the ledger checks every template it is given, whoever built it (BR-401, BR-403, BR-405).
/// </summary>
/// <param name="Kind">The financial event.</param>
/// <param name="DocumentType">The kind of document the posting belongs to.</param>
/// <param name="DocumentId">The id of that document.</param>
/// <param name="PayerId">The payer whose balances move.</param>
/// <param name="BusinessDate">The business date of the document (BR-406).</param>
/// <param name="Description">What happened, for a person reading the ledger.</param>
/// <param name="Lines">The lines: two or more, debits equal to credits.</param>
/// <param name="ReversalOf">The posting this one reverses, for a reversal.</param>
public sealed record PostingTemplate(
    LedgerPostingKind Kind,
    LedgerDocumentType DocumentType,
    Guid DocumentId,
    PayerId PayerId,
    DateOnly BusinessDate,
    string Description,
    IReadOnlyList<LedgerLine> Lines,
    LedgerPostingId? ReversalOf = null);
