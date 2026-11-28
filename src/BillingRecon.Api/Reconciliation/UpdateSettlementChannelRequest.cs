using System.Text.Json.Serialization;
using BillingRecon.Application.Reconciliation;
using BillingRecon.Domain.Common;

namespace BillingRecon.Api.Reconciliation;

/// <summary>
/// The body of <c>PATCH /api/v1/settlement-channels/{id}</c>: every field is sent, and a label left out is cleared. A
/// bank sends its mapping, a gateway its bank channel. The kind and the gateway are not part of it.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateSettlementChannelRequest
{
    /// <summary>How people name the account, unique in the hospital.</summary>
    public string? Name { get; init; }

    /// <summary>At most the last four digits of the account or merchant number.</summary>
    public string? AccountLabel { get; init; }

    /// <summary>Days before a line an item may be dated and still match it, 0 to 10; unchanged when left out.</summary>
    public int? DateWindowDays { get; init; }

    /// <summary>The bank channel a gateway settles into.</summary>
    public Guid? BankChannelId { get; init; }

    /// <summary>How a bank's CSV is read.</summary>
    public ColumnMappingDto? Mapping { get; init; }

    /// <summary>The command for channel <paramref name="id"/> at the version the client read.</summary>
    public UpdateSettlementChannelCommand ToCommand(SettlementChannelId id, string version) => new()
    {
        Id = id,
        ExpectedVersion = version,
        Name = Name ?? string.Empty,
        AccountLabel = AccountLabel,
        DateWindowDays = DateWindowDays,
        BankChannelId = BankChannelId,
        Mapping = Mapping,
    };
}
