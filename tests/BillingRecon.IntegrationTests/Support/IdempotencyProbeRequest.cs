namespace BillingRecon.IntegrationTests.Support;

/// <summary>The body of the idempotency probe: the MRN of the patient to register, and what to do after saving.</summary>
public sealed record IdempotencyProbeRequest(string Mrn, IdempotencyProbeOutcome Then = IdempotencyProbeOutcome.Succeed);
