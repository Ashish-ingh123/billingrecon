namespace BillingRecon.IntegrationTests.Support;

/// <summary>What the idempotency probe does once it has taken a number and saved its record.</summary>
public enum IdempotencyProbeOutcome
{
    /// <summary>Answer 201 with the record.</summary>
    Succeed,

    /// <summary>Answer 422 <c>probe_refused</c>.</summary>
    Refuse,

    /// <summary>Throw: a fault, 500.</summary>
    Fault,

    /// <summary>Throw while the state says so (<see cref="IdempotencyProbeState.FaultNext"/>), succeed afterwards.</summary>
    FaultOnce,

    /// <summary>Wait at the gate of the state before doing anything, then succeed.</summary>
    Wait,
}
