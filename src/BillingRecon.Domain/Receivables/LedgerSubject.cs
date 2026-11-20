namespace BillingRecon.Domain.Receivables;

/// <summary>The document a subject account is kept for: a bill, a payment or a credit note, by kind and id (BR-403).</summary>
/// <param name="Type">The kind of document.</param>
/// <param name="Id">The id of the document.</param>
public readonly record struct LedgerSubject(LedgerSubjectType Type, Guid Id);
