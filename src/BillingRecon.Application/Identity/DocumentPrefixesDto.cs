namespace BillingRecon.Application.Identity;

/// <summary>The default number prefix of each document type (BR-802): the prefix of its next new series.</summary>
public sealed record DocumentPrefixesDto(string Bill, string CreditNote, string Payment, string Adjustment, string Refund);
