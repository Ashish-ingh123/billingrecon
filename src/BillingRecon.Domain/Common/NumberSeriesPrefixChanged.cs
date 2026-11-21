namespace BillingRecon.Domain.Common;

/// <summary>The prefix of a series changed: the numbers taken from now on carry the new one (BR-802, ADR 0033).</summary>
public sealed record NumberSeriesPrefixChanged(NumberSeriesId NumberSeriesId, DocumentType DocumentType, FiscalYear FiscalYear, string FromPrefix, string ToPrefix) : IDomainEvent;
