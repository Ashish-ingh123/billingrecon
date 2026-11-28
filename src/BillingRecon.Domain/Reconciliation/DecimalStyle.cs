namespace BillingRecon.Domain.Reconciliation;

/// <summary>How a bank's CSV writes the decimals of an amount (BR-1002). Stored as text inside the column mapping.</summary>
public enum DecimalStyle
{
    /// <summary>A point before the paise, commas between the groups of digits: 1,23,456.78 or 123,456.78.</summary>
    Point,

    /// <summary>A comma before the paise, points between the groups of digits: 1.23.456,78.</summary>
    Comma,
}
