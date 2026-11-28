namespace BillingRecon.Application.Reconciliation;

/// <summary>What creating and changing a settlement channel both carry, so that one validator checks both (BR-1001, BR-1002).</summary>
public interface ISettlementChannelInput
{
    /// <summary>How people name the account, unique in the hospital.</summary>
    public string Name { get; }

    /// <summary>At most the last four digits of the account or merchant number.</summary>
    public string? AccountLabel { get; }

    /// <summary>Days before a line an item may be dated and still match it, 0 to 10; 3 when left out.</summary>
    public int? DateWindowDays { get; }

    /// <summary>The bank channel a gateway settles into; none for a bank.</summary>
    public Guid? BankChannelId { get; }

    /// <summary>The column mapping of a bank; none for a gateway.</summary>
    public ColumnMappingDto? Mapping { get; }
}
