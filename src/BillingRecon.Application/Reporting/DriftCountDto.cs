namespace BillingRecon.Application.Reporting;

/// <summary>How many drift rows one broken identity has.</summary>
public sealed record DriftCountDto(string Identity, int Rows);
