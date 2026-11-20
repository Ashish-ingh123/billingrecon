using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Payers;

/// <summary>Makes an inactive payer active again. <c>ExpectedVersion</c> is the version the client read (BR-804).</summary>
public sealed record ReactivatePayerCommand(PayerId Id, string ExpectedVersion);
