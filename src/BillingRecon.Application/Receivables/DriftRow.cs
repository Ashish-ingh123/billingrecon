namespace BillingRecon.Application.Receivables;

/// <summary>
/// One place where a cached balance and the ledger disagree, or where the ledger breaks one of its own identities
/// (BR-404; docs/database.md, sections 5.5 and 5.6). A drift row is a defect of the highest severity: it is reported,
/// never corrected by hand.
/// </summary>
/// <param name="Identity">Which identity is broken: one of the constants of <see cref="DriftIdentity"/>.</param>
/// <param name="SubjectId">The bill, payer or posting the row is about.</param>
/// <param name="Reference">The document number, where the subject has one.</param>
/// <param name="Cached">The cached amount, or the debits of an unbalanced posting.</param>
/// <param name="Ledger">The amount the ledger gives, or the credits of an unbalanced posting.</param>
public sealed record DriftRow(string Identity, Guid SubjectId, string? Reference, decimal Cached, decimal Ledger);
