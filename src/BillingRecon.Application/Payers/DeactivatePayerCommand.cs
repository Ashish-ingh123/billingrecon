using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Payers;

/// <summary>Deactivates a payer (US-12, BR-105). <c>ExpectedVersion</c> is the version the client read (BR-804).</summary>
public sealed record DeactivatePayerCommand(PayerId Id, string ExpectedVersion);
