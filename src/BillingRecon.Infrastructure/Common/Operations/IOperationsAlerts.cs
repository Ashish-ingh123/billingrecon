namespace BillingRecon.Infrastructure.Common.Operations;

/// <summary>
/// Tells the operator something is wrong that no user will see (ADR 0056): drift in the ledger, a job that failed. An
/// alert is always written to the log as an error; it is also mailed when <see cref="Delivers"/>. Its text names
/// hospitals by their code and documents by their number, never a patient or a payer.
/// </summary>
public interface IOperationsAlerts
{
    /// <summary>True when an alert is mailed as well as logged.</summary>
    public bool Delivers { get; }

    /// <summary>Raises an alert.</summary>
    public void Raise(string subject, string body);
}
