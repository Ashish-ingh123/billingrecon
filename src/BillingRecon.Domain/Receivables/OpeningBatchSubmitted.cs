using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Receivables;

/// <summary>An opening batch was submitted for approval (ADR 0053): what it would bring in, as at its cut-over date.</summary>
public sealed record OpeningBatchSubmitted(DateOnly CutOverDate, int Receivables, Money ReceivableTotal, int Advances, Money AdvanceTotal) : IDomainEvent;
