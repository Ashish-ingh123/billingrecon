using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Receivables;

/// <summary>The checker rejects an opening batch (ADR 0053), saying why: nothing of it is posted.</summary>
public sealed record RejectOpeningBatchCommand(OpeningBatchId Id, string ExpectedVersion, string? Comment);
