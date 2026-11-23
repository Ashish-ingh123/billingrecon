namespace BillingRecon.Application.Reporting;

/// <summary>
/// The ageing as of a day: the four buckets, always all four, and their total, which is the outstanding dues of that day
/// (the Receivable account, BR-404).
/// </summary>
public sealed record AgeingDto(DateOnly AsOf, IReadOnlyList<AgeingBucketDto> Buckets, int Bills, decimal Total);
