namespace BillingRecon.IntegrationTests.Support;

/// <summary>What the idempotency probe answers: the record, the number it took, and how often the action had run by then.</summary>
public sealed record IdempotencyProbeResponse(Guid Id, string Mrn, string Number, string? Tag, int Execution);
