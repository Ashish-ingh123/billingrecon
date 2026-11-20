using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payers;

namespace BillingRecon.Application.Payers;

/// <summary>
/// Creates a self-pay payer (US-11): for a patient, who then pays their own bills, or without one, as a guarantor who
/// pays for someone (BR-104). The kind is required; only <see cref="PayerKind.SelfPay"/> is accepted in R1 (BR-103).
/// </summary>
public sealed record CreatePayerCommand : IPayerDetailsInput
{
    /// <summary>The kind the client asked for; null when it sent none.</summary>
    public PayerKind? Kind { get; init; }

    /// <summary>The patient the payer is; null for a guarantor.</summary>
    public PatientId? PatientId { get; init; }

    /// <inheritdoc />
    public required string Name { get; init; }

    /// <inheritdoc />
    public string? Phone { get; init; }

    /// <inheritdoc />
    public string? Email { get; init; }
}
