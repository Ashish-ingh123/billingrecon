using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Receivables;

/// <summary>An opening batch was rejected (ADR 0053): nothing of it was posted.</summary>
public sealed record OpeningBatchRejected(OpeningBatchId BatchId, string Reason) : IDomainEvent, IHasReason;
