using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Receivables;

/// <summary>An opening batch was approved and its opening balances posted (ADR 0053).</summary>
public sealed record OpeningBatchApproved(OpeningBatchId BatchId, int Receivables, Money ReceivableTotal, int Advances, Money AdvanceTotal) : IDomainEvent;
