namespace BillingRecon.Application.Reporting;

/// <summary>
/// One bucket of the ageing: its name (<c>0-30</c>, <c>31-60</c>, <c>61-90</c>, <c>90+</c>), the bills in it, what they
/// owe, and that in percent of everything owed (two places; the shares may add up to 99.99 or 100.01).
/// </summary>
public sealed record AgeingBucketDto(string Bucket, int Bills, decimal Amount, decimal Share);
