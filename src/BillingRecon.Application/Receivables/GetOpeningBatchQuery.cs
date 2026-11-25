using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Receivables;

/// <summary>One opening batch by its id.</summary>
public sealed record GetOpeningBatchQuery(OpeningBatchId Id);
