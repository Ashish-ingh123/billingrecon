namespace BillingRecon.Application.Reporting;

/// <summary>The collection of one business date: a row for every payment method, zeros included, and the totals.</summary>
public sealed record DailyCollectionDto(DateOnly Date, IReadOnlyList<CollectionByMethodDto> Methods, CollectionTotalsDto Totals);
