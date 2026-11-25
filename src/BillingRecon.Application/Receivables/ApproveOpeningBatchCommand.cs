using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Receivables;

/// <summary>The checker approves an opening batch (ADR 0053): its opening balances are posted in the same transaction.</summary>
public sealed record ApproveOpeningBatchCommand(OpeningBatchId Id, string ExpectedVersion, string? Comment);
