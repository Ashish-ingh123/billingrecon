namespace BillingRecon.Application.Reconciliation;

/// <summary>A line read from a file, with its fingerprint, which of its fingerprint in the file it is, and whether the channel already holds it (BR-1004).</summary>
public sealed record PlannedStatementLine(ReadStatementRow Row, string Fingerprint, int Occurrence, bool AlreadyHeld);
